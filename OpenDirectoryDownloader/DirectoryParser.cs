using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.GoogleDrive;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    public static class DirectoryParser
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly HtmlParser HtmlParser = new HtmlParser();

        /// <summary>
        /// Parses Html to a WebDirectory object containing the current directory index
        /// </summary>
        /// <param name="baseUrl">Base url</param>
        /// <param name="html">Html to parse</param>
        /// <returns>WebDirectory object containing current directory index</returns>
        public static async Task<WebDirectory> ParseHtml(WebDirectory webDirectory, string html, HttpClient httpClient = null)
        {
            string baseUrl = webDirectory.Url;

            if (!baseUrl.EndsWith("/") && string.IsNullOrWhiteSpace(webDirectory.Uri.Query) && string.IsNullOrWhiteSpace(Path.GetExtension(baseUrl)))
            {
                baseUrl += "/";
            }

            WebDirectory parsedWebDirectory = new WebDirectory(webDirectory.ParentDirectory)
            {
                Url = baseUrl,
                Name = WebUtility.UrlDecode(Path.GetDirectoryName(new Uri(baseUrl).Segments.LastOrDefault())) ?? "ROOT",
                StartTime = DateTimeOffset.UtcNow,
                Finished = true
            };

            try
            {
                if (webDirectory.Uri.Host == "drive.google.com")
                {
                    return await GoogleDriveIndexer.IndexAsync(webDirectory);
                    //return GoogleDriveParser.ParseGoogleDriveHtml(html, webDirectory);
                }

                IHtmlDocument htmlDocument = await HtmlParser.ParseDocumentAsync(html);

                if (webDirectory.Uri.Host == "ipfs.io" || webDirectory.Uri.Host == "gateway.ipfs.io")
                {
                    return ParseIpfsDirectoryListing(baseUrl, parsedWebDirectory, htmlDocument);
                }

                htmlDocument.QuerySelectorAll("#sidebar").ToList().ForEach(e => e.Remove());

                // The order of the checks are very important!

                IHtmlCollection<IElement> directoryListingDotComlistItems = htmlDocument.QuerySelectorAll("#directory-listing li");

                if (directoryListingDotComlistItems.Any())
                {
                    return ParseDirectoryListingDoctComDirectoryListing(baseUrl, parsedWebDirectory, directoryListingDotComlistItems);
                }

                IHtmlCollection<IElement> h5aiTableRows = htmlDocument.QuerySelectorAll("#fallback table tr");

                if (h5aiTableRows.Any())
                {
                    return ParseH5aiDirectoryListing(baseUrl, parsedWebDirectory, h5aiTableRows);
                }

                // Snif directory listing
                // http://web.archive.org/web/20140724000351/http://bitfolge.de/index.php?option=com_content&view=article&id=66:snif-simple-and-nice-index-file&catid=38:eigene&Itemid=59
                IHtmlCollection<IElement> snifTableRows = htmlDocument.QuerySelectorAll("table.snif tr");

                if (snifTableRows.Any())
                {
                    return ParseSnifDirectoryListing(baseUrl, parsedWebDirectory, snifTableRows);
                }

                // Godir - https://gitlab.com/Montessquio/godir
                IHtmlCollection<IElement> pureTableRows = htmlDocument.QuerySelectorAll("table.listing-table tbody tr");

                if (pureTableRows.Any())
                {
                    return ParsePureDirectoryListing(ref baseUrl, parsedWebDirectory, htmlDocument, pureTableRows);
                }

                // Remove it after ParsePureDirectoryListing (.breadcrumb is used in it)
                htmlDocument.QuerySelectorAll(".breadcrumb").ToList().ForEach(e => e.Remove());

                // Custom directory listing
                IHtmlCollection<IElement> divElements = htmlDocument.QuerySelectorAll("div#listing div");

                if (divElements.Any())
                {
                    return ParseCustomDivListing(ref baseUrl, parsedWebDirectory, htmlDocument, divElements);
                }

                IHtmlCollection<IElement> pres = htmlDocument.QuerySelectorAll("pre");

                if (pres.Any())
                {
                    WebDirectory result = await ParsePreDirectoryListing(baseUrl, parsedWebDirectory, pres);

                    if (result.Files.Any() || result.Subdirectories.Any() || result.Error)
                    {
                        return result;
                    }
                }

                IHtmlCollection<IElement> tables = htmlDocument.QuerySelectorAll("table");

                if (tables.Any())
                {
                    WebDirectory result = ParseTablesDirectoryListing(baseUrl, parsedWebDirectory, tables);

                    if (result.ParsedSuccesfully || result.Error)
                    {
                        return result;
                    }
                }

                IHtmlCollection<IElement> materialDesignListItems = htmlDocument.QuerySelectorAll("ul.mdui-list li");

                if (materialDesignListItems.Any())
                {
                    return ParseMaterialDesignListItemsDirectoryListing(baseUrl, parsedWebDirectory, materialDesignListItems);
                }

                IHtmlCollection<IElement> listItems = htmlDocument.QuerySelectorAll("ul li");

                if (listItems.Any())
                {
                    WebDirectory result = ParseListItemsDirectoryListing(baseUrl, parsedWebDirectory, listItems);

                    if (result.ParsedSuccesfully || result.Error)
                    {
                        return result;
                    }
                }

                // Latest fallback
                IHtmlCollection<IElement> links = htmlDocument.QuerySelectorAll("a");

                if (links.Any())
                {
                    parsedWebDirectory = ParseLinksDirectoryListing(baseUrl, parsedWebDirectory, links);
                }

                parsedWebDirectory = await ParseDirectoryListingModel01(baseUrl, parsedWebDirectory, htmlDocument, httpClient);

                CheckParsedResults(parsedWebDirectory);

                return parsedWebDirectory;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);

                parsedWebDirectory.Error = true;
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static async Task<WebDirectory> ParseDirectoryListingModel01(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, HttpClient httpClient)
        {
            // If anyone knows which directory listing this is... :P
            IElement fileManager = htmlDocument.QuerySelector("div.filemanager");

            if (fileManager != null)
            {
                IElement script = htmlDocument.QuerySelector("script[src*=\"script.js\"]");

                if (script != null)
                {
                    Uri scriptUri = new Uri(new Uri(baseUrl), script.Attributes["src"].Value);
                    string scriptBody = await httpClient.GetStringAsync(scriptUri);

                    Match regexMatch = new Regex(@"\$\.get\('(?<DirectoryIndexFile>.*)',").Match(scriptBody);

                    if (regexMatch.Success)
                    {
                        Uri directoryIndexFile = new Uri(new Uri(baseUrl), regexMatch.Groups["DirectoryIndexFile"].Value);

                        string directoryIndexJson = await httpClient.GetStringAsync(directoryIndexFile);

                        DirectoryListingModel01 directoryListingModel = JsonConvert.DeserializeObject<DirectoryListingModel01>(directoryIndexJson);

                        WebDirectory newWebDirectory = ConvertDirectoryListingModel01(baseUrl, parsedWebDirectory, directoryListingModel);
                        parsedWebDirectory.Description = newWebDirectory.Description;
                        parsedWebDirectory.StartTime = newWebDirectory.StartTime;
                        parsedWebDirectory.Files = newWebDirectory.Files;
                        parsedWebDirectory.Finished = newWebDirectory.Finished;
                        parsedWebDirectory.Name = newWebDirectory.Name;
                        parsedWebDirectory.Subdirectories = newWebDirectory.Subdirectories;
                        parsedWebDirectory.Url = newWebDirectory.Url;
                        parsedWebDirectory.ParsedSuccesfully = true;
                        parsedWebDirectory.Parser = "DirectoryListingModel01";
                    }
                }
            }

            return parsedWebDirectory;
        }

        private static WebDirectory ConvertDirectoryListingModel01(string baseUrl, WebDirectory parsedWebDirectory, DirectoryListingModel01 directoryListingModel)
        {
            Uri directoryUri = new Uri(new Uri(baseUrl), directoryListingModel.Path);
            string directoryFullUrl = directoryUri.ToString();

            WebDirectory webDirectory = new WebDirectory(parsedWebDirectory.ParentDirectory)
            {
                Url = directoryFullUrl,
                Name = directoryListingModel.Name,
                StartTime = DateTimeOffset.UtcNow,
                Finished = true
            };

            foreach (DirectoryListingModel01 item in directoryListingModel.Items)
            {
                Uri uri = new Uri(new Uri(baseUrl), item.Path);
                string itemFullUrl = uri.ToString();

                if (item.Type == "folder")
                {
                    WebDirectory newWebDirectory = ConvertDirectoryListingModel01(baseUrl, webDirectory, item);
                    webDirectory.Subdirectories.Add(newWebDirectory);
                }
                else
                {
                    webDirectory.Files.Add(new WebFile
                    {
                        Url = itemFullUrl,
                        FileName = Path.GetFileName(itemFullUrl),
                        FileSize = item.Size
                    });
                }
            }

            return webDirectory;
        }

        private static WebDirectory ParseCustomDivListing(ref string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> divElements)
        {
            foreach (IElement divElement in divElements)
            {
                string size = divElement.QuerySelector("em").TextContent.Trim();

                bool isFile = IsFileSize(size);

                IElement link = divElement.QuerySelector("a");
                string linkHref = link.Attributes["href"].Value;

                if (IsValidLink(link))
                {
                    Uri uri = new Uri(new Uri(baseUrl), linkHref);
                    string fullUrl = uri.ToString();

                    if (!isFile)
                    {
                        parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                        {
                            Parser = "ParseDivListing",
                            Url = fullUrl,
                            Name = link.QuerySelector("strong").TextContent.TrimEnd('/')
                        });
                    }
                    else
                    {
                        parsedWebDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                            FileSize = FileSizeHelper.ParseFileSize(size)
                        });
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseIpfsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument)
        {
            foreach (IElement tableRow in htmlDocument.QuerySelectorAll("table tr"))
            {
                string size = tableRow.QuerySelector("td:nth-child(3)").TextContent.Trim();

                bool isFile = IsFileSize(size);

                IElement link = tableRow.QuerySelector("a");
                string linkHref = link.Attributes["href"].Value;

                if (IsValidLink(link))
                {
                    Uri uri = new Uri(new Uri(baseUrl), linkHref);
                    string fullUrl = uri.ToString();

                    if (!isFile)
                    {
                        parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                        {
                            Parser = "ParseIpfsDirectoryListing",
                            Url = fullUrl,
                            Name = link.TextContent
                        });
                    }
                    else
                    {
                        parsedWebDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                            FileSize = FileSizeHelper.ParseFileSize(size)
                        });
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseDirectoryListingDoctComDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems)
        {
            foreach (IElement listItem in listItems)
            {
                string size = listItem.QuerySelector(".file-size").TextContent.Trim();

                bool isFile = IsFileSize(size);

                IElement link = listItem.QuerySelector("a");
                string linkHref = link.Attributes["href"].Value;

                if (IsValidLink(link))
                {
                    Uri uri = new Uri(new Uri(baseUrl), linkHref);
                    string fullUrl = uri.ToString();

                    if (!isFile)
                    {
                        if (listItem.Attributes["data-name"].Value != "..")
                        {
                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = "ParseDirectoryListingDoctComDirectoryListing",
                                Url = fullUrl,
                                Name = listItem.Attributes["data-name"].Value
                            });
                        }
                    }
                    else
                    {
                        parsedWebDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                            FileSize = FileSizeHelper.ParseFileSize(size)
                        });
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseSnifDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> snifTableRows)
        {
            IElement table = snifTableRows.First().Parent("table");

            Dictionary<int, TableHeaderInfo> tableHeaders = GetTableHeaders(table);

            KeyValuePair<int, TableHeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileSize);
            int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

            KeyValuePair<int, TableHeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileName);
            int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

            foreach (IElement tableRow in snifTableRows)
            {
                if (!tableRow.ClassList.Contains("snHeading") && !tableRow.QuerySelector("td").ClassList.Contains("snDir"))
                {
                    string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();

                    bool isFile = IsFileSize(size) && !size.Contains("item");

                    IHtmlAnchorElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a") as IHtmlAnchorElement;
                    string linkHref = link?.Attributes["href"].Value;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = "ParseSnifDirectoryListing",
                                Url = fullUrl,
                                Name = link.Title
                            });
                        }
                        else
                        {
                            parsedWebDirectory.Files.Add(new WebFile
                            {
                                Url = fullUrl,
                                FileName = link.Title,
                                FileSize = long.Parse(string.Join(null, Regex.Split((tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex}) span") as IHtmlSpanElement).Title, "[^\\d]")))
                            });
                        }
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParsePureDirectoryListing(ref string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> pureTableRows)
        {
            string urlFromBreadcrumbs = string.Join("/", htmlDocument.QuerySelectorAll(".breadcrumbs_main .breadcrumb").Where(b => !b.ClassList.Contains("smaller")).Select(b => b.TextContent.Trim())) + "/";

            // Remove possible file part (index.html) from url
            if (!string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(baseUrl))))
            {
                baseUrl = baseUrl.Replace(Path.GetFileName(WebUtility.UrlDecode(baseUrl)), string.Empty);
            }

            // /. is for problem displaying a folder starting with a dot
            string urlFromBaseUrl = baseUrl.Remove(0, new Uri(baseUrl).Scheme.Length + new Uri(baseUrl).Host.Length + 3).Replace("/.", "/");
            urlFromBaseUrl = urlFromBaseUrl.Replace("%23", "#");

            if (urlFromBreadcrumbs == urlFromBaseUrl)
            {
                IElement table = pureTableRows.First().Parent("table");

                Dictionary<int, TableHeaderInfo> tableHeaders = GetTableHeaders(table);

                KeyValuePair<int, TableHeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileSize);
                int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

                KeyValuePair<int, TableHeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileName);
                int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

                foreach (IElement tableRow in pureTableRows)
                {
                    string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();

                    bool isFile = !tableRow.ClassList.Contains("dir");

                    IElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a");
                    string linkHref = link.TextContent;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            if (fullUrl.Contains("#"))
                            {
                                fullUrl = fullUrl.Replace("#", "%23");
                            }

                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = "ParsePureDirectoryListing",
                                Url = $"{fullUrl}/",
                                Name = link.TextContent.Trim()
                            });
                        }
                        else
                        {
                            parsedWebDirectory.Files.Add(new WebFile
                            {
                                Url = fullUrl,
                                FileName = link.TextContent.Trim(),
                                FileSize = FileSizeHelper.ParseFileSize(tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})").TextContent)
                            });
                        }
                    }
                }
            }
            else
            {
                parsedWebDirectory.Error = true;
                Logger.Error($"Directory listing returns different directory than requested! Expected: {urlFromBaseUrl}, Actual: {urlFromBreadcrumbs}");
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseH5aiDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> h5aiTableRows)
        {
            IElement table = h5aiTableRows.First().Parent("table");

            Dictionary<int, TableHeaderInfo> tableHeaders = GetTableHeaders(table);

            KeyValuePair<int, TableHeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileSize);
            int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

            KeyValuePair<int, TableHeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileName);
            int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

            foreach (IElement tableRow in h5aiTableRows)
            {
                string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();

                bool isFile = !string.IsNullOrWhiteSpace(size);
                IElement image = tableRow.QuerySelector("img");

                if (isFile && image != null && image.HasAttribute("alt") && image.Attributes["alt"].Value == "folder")
                {
                    isFile = false;
                }

                IHtmlAnchorElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a") as IHtmlAnchorElement;
                string linkHref = link?.Attributes["href"]?.Value;

                if (link != null && IsValidLink(link))
                {
                    Uri uri = new Uri(new Uri(baseUrl), linkHref);
                    string fullUrl = uri.ToString();

                    if (!isFile)
                    {
                        parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                        {
                            Parser = "ParseH5aiDirectoryListing",
                            Url = fullUrl,
                            Name = link.TextContent.Trim()
                        });
                    }
                    else
                    {
                        parsedWebDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = link.TextContent.Trim(),
                            FileSize = FileSizeHelper.ParseFileSize(tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})").TextContent)
                        });
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseTablesDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> tables)
        {
            foreach (IElement table in tables)
            {
                // Not needed anymore
                // Skip when there is another table in it
                //if (table.QuerySelector("table") != null)
                //{
                //    continue;
                //}

                Dictionary<int, TableHeaderInfo> tableHeaders = GetTableHeaders(table);

                KeyValuePair<int, TableHeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileSize);
                int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

                KeyValuePair<int, TableHeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.FileName);
                int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

                KeyValuePair<int, TableHeaderInfo> descriptionHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == TableHeaderType.Description);
                int descriptionHeaderColumnIndex = descriptionHeader.Value != null ? descriptionHeader.Key : 0;

                if (fileSizeHeaderColumnIndex == 0 && nameHeaderColumnIndex == 0)
                {
                    if (table.QuerySelector("a") != null)
                    {
                        parsedWebDirectory = ParseLinksDirectoryListing(baseUrl, parsedWebDirectory, table.QuerySelectorAll("a"));
                    }
                }
                else
                {
                    parsedWebDirectory.ParsedSuccesfully = true;

                    // Dirty solution..
                    bool hasSeperateDirectoryAndFilesTables = false;

                    foreach (IElement tableRow in table.QuerySelectorAll("tr"))
                    {
                        if (tableRow.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                            tableRow.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                            tableRow.QuerySelector("a") != null &&
                            !tableRow.ClassList.Contains("snHeading") &&
                            tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})") != null &&
                            !tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})").TextContent.ToLower().Contains("parent directory") &&
                            tableRow.QuerySelector("table") == null)
                        {
                            IElement link = tableRow.QuerySelector("a");

                            if (IsValidLink(link))
                            {
                                string linkHref = link.Attributes["href"].Value;
                                Uri uri = new Uri(new Uri(baseUrl), linkHref);
                                string fullUrl = uri.ToString();

                                fullUrl = StripUrl(fullUrl);

                                IElement imageElement = tableRow.QuerySelector("img");
                                bool isDirectory = imageElement != null &&
                                    (
                                        (imageElement.HasAttribute("alt") && imageElement.Attributes["alt"].Value == "[DIR]") ||
                                        (imageElement.HasAttribute("src") && (Path.GetFileName(imageElement.Attributes["src"].Value).Contains("dir") || Path.GetFileName(imageElement.Attributes["src"].Value).Contains("folder")))
                                    );

                                UrlEncodingParser urlEncodingParser = new UrlEncodingParser(fullUrl);

                                string description = tableRow.QuerySelector($"td:nth-child({descriptionHeaderColumnIndex})")?.TextContent.Trim();
                                string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim().Replace(" ", string.Empty);

                                bool isFile =
                                    urlEncodingParser["file"] != null ||
                                    !isDirectory &&
                                    (urlEncodingParser["dir"] == null && (
                                        (fileSizeHeader.Value == null && !linkHref.EndsWith("/")) ||
                                        (IsFileSize(size) && size != "0.00b" && !string.IsNullOrWhiteSpace(size) && (size?.Contains("item")).Value != true && !linkHref.EndsWith("/"))
                                    ));

                                if (!isFile)
                                {
                                    string directoryName = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()));

                                    // Fallback..
                                    if (string.IsNullOrWhiteSpace(directoryName))
                                    {
                                        directoryName = uri.Segments.Last();
                                    }

                                    if (urlEncodingParser["dir"] != null)
                                    {
                                        hasSeperateDirectoryAndFilesTables = true;
                                        directoryName = link.TextContent.Trim();
                                    }

                                    if (urlEncodingParser["directory"] != null)
                                    {
                                        directoryName = link.TextContent.Trim();
                                    }

                                    if (urlEncodingParser["folder"] != null)
                                    {
                                        if (Library.IsBase64(urlEncodingParser["folder"]))
                                        {
                                            directoryName = Encoding.UTF8.GetString(Convert.FromBase64String(urlEncodingParser["folder"]));
                                        }
                                        else
                                        {
                                            directoryName = link.TextContent.Trim();
                                        }
                                    }

                                    parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                                    {
                                        Parser = "ParseTablesDirectoryListing",
                                        Url = fullUrl,
                                        Name = WebUtility.UrlDecode(directoryName),
                                        Description = description
                                    });
                                }
                                else
                                {
                                    parsedWebDirectory.Parser = "ParseTablesDirectoryListing";

                                    string filename = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath));

                                    if (urlEncodingParser["file"] != null)
                                    {
                                        filename = Path.GetFileName(urlEncodingParser["file"]);
                                    }

                                    if (string.IsNullOrWhiteSpace(filename))
                                    {
                                        filename = link.TextContent;
                                    }

                                    if (urlEncodingParser.Count == 0 && filename.ToLower() == "index.php")
                                    {
                                        continue;
                                    }

                                    parsedWebDirectory.Files.Add(new WebFile
                                    {
                                        Url = fullUrl,
                                        FileName = filename,
                                        FileSize = FileSizeHelper.ParseFileSize(size),
                                        Description = description
                                    });
                                }
                            }
                        }
                    }

                    if ((parsedWebDirectory.Files.Any() || parsedWebDirectory.Subdirectories.Any()) && !hasSeperateDirectoryAndFilesTables)
                    {
                        // Break is results are found
                        break;
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser1 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"(?:<img.*>\s*)+<a.*?>.*?<\/a>\S*\s*(?<Modified>\d*-(?:[a-zA-Z]*|\d*)-\d*\s*\d*:\d*(:\d*)?)?\s*(?<FileSize>\S+)(\s*(?<Description>.*))?");

            if (match.Success)
            {
                bool isFile = IsFileSize(match.Groups["FileSize"].Value.Trim());

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                    parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                    parsedLine.QuerySelector("a") != null &&
                    !line.ToLower().Contains("parent directory"))
                {
                    IElement link = parsedLine.QuerySelector("a");
                    string linkHref = link.Attributes["href"].Value;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = "RegexParser1",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
                                Description = match.Groups["Description"].Value.Trim()
                            });
                        }
                        else
                        {
                            try
                            {
                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
                                    Description = match.Groups["Description"].Value.Trim()
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser1");
                            }
                        }
                    }
                }
            }

            return match.Success;
        };

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser2 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"<a.*<\/a>\s*(?<DateTime>\d+-\w+-\d+\s\d+:\d{0,2})\s*(?<FileSize>\S+\s?\S*)\s*\S*");

            if (match.Success)
            {
                string fileSizeGroup = match.Groups["FileSize"].Value.Trim();

                bool isFile = long.TryParse(fileSizeGroup, out long fileSize);

                if (!isFile && IsFileSize(fileSizeGroup))
                {
                    fileSize = FileSizeHelper.ParseFileSize(fileSizeGroup);
                    isFile = fileSize > 0;
                }

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);
                IElement link = parsedLine.QuerySelector("a");
                string linkHref = link.Attributes["href"].Value;

                Uri uri = new Uri(new Uri(baseUrl), linkHref);
                string fullUrl = uri.ToString();

                if (IsValidLink(link))
                {
                    if (!isFile)
                    {
                        webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                        {
                            Parser = "RegexParser2",
                            Url = fullUrl,
                            Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()))
                        });
                    }
                    else
                    {
                        webDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                            FileSize = fileSize
                        });
                    }
                }
            }

            return match.Success;
        };

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser3 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"(?<Modified>\d+[\.-](?:[a-zA-Z]*|\d+)[\.-]\d+(?:\s*\d*:\d*(?::\d*)?)?)(?:<img.*>\s*)?\S*\s*(?<FileSize>\S+)\s*?<[aA].*<\/[aA]>");

            if (match.Success)
            {
                bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;" && match.Groups["FileSize"].Value.Trim() != "DIR";

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                    parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                    parsedLine.QuerySelector("a") != null &&
                    !line.ToLower().Contains("parent directory"))
                {
                    IElement link = parsedLine.QuerySelector("a");
                    string linkHref = link.Attributes["href"].Value;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = "RegexParser3",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
                                //Description = match.Groups["Description"].Value.Trim()
                            });
                        }
                        else
                        {
                            try
                            {
                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
                                    //Description = match.Groups["Description"].Value.Trim()
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser3");
                            }
                        }
                    }
                }
            }

            return match.Success;
        };

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser4 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"\s*(?<Modified>[A-z]*,\s*[A-z]*\s*\d*, \d*\s*\d*:\d*\s*[APM]*)\s+(?<FileSize>\S*)\s+<a.*<\/a>");

            if (match.Success)
            {
                bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                    parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                    parsedLine.QuerySelector("a") != null &&
                    !line.ToLower().Contains("parent directory"))
                {
                    IElement link = parsedLine.QuerySelector("a");
                    string linkHref = link.Attributes["href"].Value;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = "RegexParser4",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
                                //Description = match.Groups["Description"].Value.Trim()
                            });
                        }
                        else
                        {
                            try
                            {
                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
                                    //Description = match.Groups["Description"].Value.Trim()
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser4");
                            }
                        }
                    }
                }
            }

            return match.Success;
        };

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser5 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"\s*(?<Modified>\d*-\d*-\d*\s*[오전후]*\s*\d*:\d*)\s*(?<FileSize>\S*)\s+<[aA].*<\/[aA]>");

            if (match.Success)
            {
                bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                    parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                    parsedLine.QuerySelector("a") != null &&
                    !line.ToLower().Contains("parent directory"))
                {
                    IElement link = parsedLine.QuerySelector("a");
                    string linkHref = link.Attributes["href"].Value;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = "RegexParser5",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
                                //Description = match.Groups["Description"].Value.Trim()
                            });
                        }
                        else
                        {
                            try
                            {
                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
                                    //Description = match.Groups["Description"].Value.Trim()
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser5");
                            }
                        }
                    }
                }
            }

            return match.Success;
        };

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser6 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"(?<Modified>\d+\/\d+\/\d+\s*\d+:\d+\s+[APM]+)\s+(?<FileSize>\S*)\s*<a.*<\/a>");

            if (match.Success)
            {
                bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                    parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                    parsedLine.QuerySelector("a") != null &&
                    !line.ToLower().Contains("parent directory"))
                {
                    IElement link = parsedLine.QuerySelector("a");
                    string linkHref = link.Attributes["href"].Value;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = "RegexParser6",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
                                //Description = match.Groups["Description"].Value.Trim()
                            });
                        }
                        else
                        {
                            try
                            {
                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
                                    //Description = match.Groups["Description"].Value.Trim()
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser6");
                            }
                        }
                    }
                }
            }

            return match.Success;
        };

        private static async Task<WebDirectory> ParsePreDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> pres)
        {
            List<Func<WebDirectory, string, string, Task<bool>>> regexFuncs = new List<Func<WebDirectory, string, string, Task<bool>>>
            {
                RegexParser1,
                RegexParser2,
                RegexParser3,
                RegexParser4,
                RegexParser5,
                RegexParser6,
            };

            foreach (IElement pre in pres)
            {
                List<string> lines = Regex.Split(pre.InnerHtml, "\r\n|\r|\n|<br\\S*>|<hr>").ToList();

                foreach (string line in lines)
                {
                    foreach (Func<WebDirectory, string, string, Task<bool>> regexFunc in regexFuncs)
                    {
                        bool succeeded = await regexFunc(parsedWebDirectory, baseUrl, line);

                        if (succeeded)
                        {
                            parsedWebDirectory.Parser = "ParsePreDirectoryListing";
                            break;
                        }
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseMaterialDesignListItemsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems)
        {
            int nameIndex = -1;
            int sizeIndex = -1;
            int modifiedIndex = -1;

            IElement headerListItem = listItems.First(li => li.ClassList.Contains("th"));

            int columnIndex = 0;

            foreach (IElement div in headerListItem.QuerySelectorAll("div"))
            {
                IElement italicElement = div.QuerySelector("i");

                if (italicElement != null)
                {
                    IAttr dataSortAttribure = italicElement.Attributes["data-sort"];

                    if (dataSortAttribure != null)
                    {
                        if (dataSortAttribure.Value == "name")
                        {
                            nameIndex = columnIndex;
                        }

                        if (dataSortAttribure.Value == "date")
                        {
                            modifiedIndex = columnIndex;
                        }

                        if (dataSortAttribure.Value == "size")
                        {
                            sizeIndex = columnIndex;
                        }
                    }
                }

                IElement linkElement = div.QuerySelector("a");

                if (linkElement != null)
                {
                    UrlEncodingParser urlEncodingParser = new UrlEncodingParser(linkElement.Attributes["href"].Value);

                    string sortBy = urlEncodingParser["sortby"];

                    if (sortBy == "name")
                    {
                        nameIndex = columnIndex;
                    }

                    if (sortBy == "lastModtime")
                    {
                        modifiedIndex = columnIndex;
                    }

                    if (sortBy == "size")
                    {
                        sizeIndex = columnIndex;
                    }
                }

                columnIndex++;
            }

            // Format 1
            //<li class="mdui-list-item th">
            //  <div class="mdui-col-xs-12 mdui-col-sm-7">文件 <i class="mdui-icon material-icons icon-sort" data-sort="name" data-order="downward">expand_more</i></div>
            //  <div class="mdui-col-sm-3 mdui-text-right">修改时间 <i class="mdui-icon material-icons icon-sort" data-sort="date" data-order="downward">expand_more</i></div>
            //  <div class="mdui-col-sm-2 mdui-text-right">大小 <i class="mdui-icon material-icons icon-sort" data-sort="size" data-order="downward">expand_more</i></div>
            //</li>

            // Format 2
            //<li class="mdui-list-item th">
            //	<div class="mdui-col-xs-8 mdui-col-sm-5"><a href="/A:?order=asc&sortby=name">文件</a></div>
            //	<div class="mdui-col-xs-4 mdui-col-sm-2"><a href="/A:?order=asc&sortby=lastModtime">修改时间<i class="mdui-icon material-icons">arrow_downward</i></a></div>
            //	<div class="mdui-col-sm-2 mdui-text-right"><a href="/A:?order=asc&sortby=type">文件类型</a></div>
            //	<div class="mdui-col-sm-2 mdui-text-right"><a href="/A:?order=asc&sortby=size">大小</a></div>
            //</li>

            foreach (IElement listItem in listItems.Where(li => li.ClassList.Contains("mdui-list-item") && !li.ClassList.Contains("th")))
            {
                IElement italicItem = listItem.QuerySelector("i");

                if (italicItem != null && italicItem.TextContent.Trim() == "arrow_upward")
                {
                    continue;
                }

                foreach (IElement removeItem in listItem.QuerySelectorAll("i"))
                {
                    removeItem.Remove();
                }

                IElement link = listItem.QuerySelector("a");

                if (link?.Attributes["href"] != null)
                {
                    string linkHref = link.Attributes["href"]?.Value;

                    bool isFile = listItem.ClassList.Contains("file");

                    if (IsValidLink(link))
                    {
                        string name = string.Empty;

                        IElement nameElement = listItem.QuerySelector(".mdui-text-truncate");

                        if (nameElement != null)
                        {
                            name = nameElement.TextContent.Trim();
                        }

                        if (listItem.Attributes["data-sort-name"] != null)
                        {
                            name = listItem.Attributes["data-sort-name"].Value;
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = link.QuerySelector($"div:nth-child({nameIndex + 1})")?.TextContent.Trim();
                        }

                        string modified = link.QuerySelector($"div:nth-child({modifiedIndex + 1})")?.TextContent.Trim();

                        if (listItem.Attributes["data-sort-date"] != null)
                        {
                            modified = Library.UnixTimestampToDateTime(long.Parse(listItem.Attributes["data-sort-date"].Value)).ToString();
                        }

                        string fileSize = link.QuerySelector($"div:nth-child({sizeIndex + 1})")?.TextContent.Trim();

                        if (listItem.Attributes["data-sort-size"] != null)
                        {
                            fileSize = listItem.Attributes["data-sort-size"].Value;
                        }

                        Uri uri = new Uri(new Uri(baseUrl), linkHref);
                        string fullUrl = uri.ToString();

                        if (!isFile)
                        {
                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = "ParseMaterialDesignListItemsDirectoryListing",
                                Url = fullUrl,
                                Name = name
                            });
                        }
                        else
                        {
                            parsedWebDirectory.Files.Add(new WebFile
                            {
                                Url = fullUrl,
                                FileName = name,
                                FileSize = FileSizeHelper.ParseFileSize(fileSize)
                            });
                        }
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseListItemsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems)
        {
            bool firstLink = true;

            foreach (IElement listItem in listItems)
            {
                if (firstLink)
                {
                    firstLink = false;

                    if (listItem.TextContent.Contains("Parent"))
                    {
                        continue;
                    }
                }

                IElement link = listItem.QuerySelector("a");

                if (link != null)
                {
                    ProcessLink(baseUrl, parsedWebDirectory, link, "ParseListItemsDirectoryListing");
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseLinksDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> links)
        {
            foreach (IElement link in links)
            {
                ProcessLink(baseUrl, parsedWebDirectory, link, "ParseLinksDirectoryListing");
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static void ProcessLink(string baseUrl, WebDirectory parsedWebDirectory, IElement link, string parser)
        {
            if (link.HasAttribute("href"))
            {
                string linkHref = link.Attributes["href"].Value;

                if (IsValidLink(link))
                {
                    Uri uri = new Uri(new Uri(baseUrl), linkHref);
                    string fullUrl = uri.ToString();

                    fullUrl = StripUrl(fullUrl);

                    UrlEncodingParser urlEncodingParser = new UrlEncodingParser(fullUrl);

                    if (uri.Segments.Length == 1 && uri.Segments.Last() == "/" && urlEncodingParser["dir"] == null)
                    {
                        return;
                    }

                    if (baseUrl == StripUrl(fullUrl))
                    {
                        return;
                    }

                    parsedWebDirectory.ParsedSuccesfully = true;

                    bool directoryListAsp = Path.GetFileName(fullUrl) == "DirectoryList.asp" || fullUrl.Contains("DirectoryList.asp");
                    bool dirParam = urlEncodingParser["dir"] != null;

                    if (!string.IsNullOrWhiteSpace(Path.GetExtension(fullUrl)) && !directoryListAsp && !dirParam)
                    {
                        parsedWebDirectory.Parser = parser;

                        long fileSize = FileSizeHelper.ParseFileSize(link.ParentElement?.QuerySelector(".fileSize")?.TextContent);

                        parsedWebDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = Path.GetFileName(WebUtility.UrlDecode(linkHref)),
                            FileSize = fileSize,
                        });
                    }
                    else
                    {
                        parsedWebDirectory.Parser = parser;

                        if (dirParam)
                        {
                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = parser,
                                Url = fullUrl,
                                Name = urlEncodingParser["dir"]
                            });
                        }
                        else if (!directoryListAsp)
                        {
                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = parser,
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(uri.Segments.Last()).Trim().TrimEnd(new char[] { '/' })
                            });
                        }
                        else
                        {
                            parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                            {
                                Parser = parser,
                                Url = fullUrl,
                                Name = link.TextContent.Trim().TrimEnd(new char[] { '/' })
                            });
                        }
                    }
                }
            }
        }

        private static void CheckParsedResults(WebDirectory webDirectory)
        {
            if (!webDirectory.Subdirectories.Any() && !webDirectory.Files.Any())
            {
                return;
            }

            foreach (WebDirectory webDirectorySub in webDirectory.Subdirectories)
            {
                webDirectorySub.Url = StripUrl(webDirectorySub.Url);
            }

            CleanFragments(webDirectory);

            CheckSymlinks(webDirectory);
        }

        private static void CleanFragments(WebDirectory webDirectory)
        {
            // Directories
            List<WebDirectory> directoriesWithFragments = webDirectory.Subdirectories.Where(wd => wd.Url.Contains("#")).ToList();

            if (directoriesWithFragments.Any())
            {
                List<WebDirectory> clientWebDirs = new List<WebDirectory>();

                foreach (WebDirectory webDir in directoriesWithFragments)
                {
                    webDirectory.Subdirectories.Remove(webDir);
                }

                foreach (WebDirectory directoryWithFragments in directoriesWithFragments)
                {
                    Uri uri = new Uri(directoryWithFragments.Url);
                    directoryWithFragments.Url = uri.GetLeftPart(UriPartial.Query);

                    if (!webDirectory.Subdirectories.Any(wd => wd.Url == directoryWithFragments.Url))
                    {
                        webDirectory.Subdirectories.Add(directoryWithFragments);
                    }
                }
            }

            // Files
            List<WebFile> filesWithFragments = webDirectory.Files.Where(wf => wf.Url.Contains("#")).ToList();

            if (filesWithFragments.Any())
            {
                List<WebFile> cleanFiles = new List<WebFile>();

                webDirectory.Files.RemoveAll(wf => wf.Url.Contains("#"));

                foreach (WebFile fileWithFragment in filesWithFragments)
                {
                    Uri uri = new Uri(fileWithFragment.Url);
                    fileWithFragment.Url = uri.GetLeftPart(UriPartial.Query);

                    if (!webDirectory.Files.Any(wf => wf.Url == fileWithFragment.Url))
                    {
                        webDirectory.Files.Add(fileWithFragment);
                    }
                }
            }
        }

        private static string StripUrl(string url)
        {
            UrlEncodingParser urlEncodingParser = new UrlEncodingParser(url);

            if (urlEncodingParser.Count == 2 && urlEncodingParser.Get("C") != null && urlEncodingParser.Get("O") != null)
            {
                // Remove the default C (column) and O (order) parameters
                urlEncodingParser.Remove("C");
                urlEncodingParser.Remove("O");

                return urlEncodingParser.ToString();
            }

            return url;
        }

        private static void CheckSymlinks(WebDirectory webDirectory)
        {
            WebDirectory parentWebDirectory = webDirectory.ParentDirectory;

            for (int level = 1; level <= 4; level++)
            {
                if (webDirectory.Uri.Segments.Length > level && parentWebDirectory != null)
                {
                    if (webDirectory.Subdirectories.Any() || webDirectory.Files.Any())
                    {
                        if (CheckDirectoryTheSame(webDirectory, parentWebDirectory))
                        {
                            Logger.Error($"Possible virtual directory or symlink detected (level {level})! SKIPPING! Url: {webDirectory.Url}");

                            webDirectory.Subdirectories = new ConcurrentList<WebDirectory>();
                            webDirectory.Files = new List<WebFile>();
                            webDirectory.Error = true;
                            break;
                        }
                    }

                    parentWebDirectory = parentWebDirectory?.ParentDirectory;
                }
            }
        }

        private static bool CheckDirectoryTheSame(WebDirectory webDirectory, WebDirectory parentWebDirectory)
        {
            if (webDirectory.Files.Count == parentWebDirectory.Files.Count &&
                webDirectory.Subdirectories.Count == parentWebDirectory.Subdirectories.Count)
            {
                // TODO: If anyone knows a nice way without JsonConvert, PR!
                if (JsonConvert.SerializeObject(parentWebDirectory.Files.Select(f => new { f.FileName, f.FileSize })) == JsonConvert.SerializeObject(webDirectory.Files.Select(f => new { f.FileName, f.FileSize })) &&
                    JsonConvert.SerializeObject(parentWebDirectory.Subdirectories.Select(d => d.Name)) == JsonConvert.SerializeObject(webDirectory.Subdirectories.Select(d => d.Name)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check simple cases of file size
        /// </summary>
        /// <param name="value">Value to check</param>
        /// <returns>Is it might be a file size</returns>
        private static bool IsFileSize(string value)
        {
            return value != "-" && value != "—" && value != "<Directory>";
        }

        public enum TableHeaderType
        {
            Unknown,
            FileName,
            FileSize,
            Modified,
            Description,
            Type
        }

        [DebuggerDisplay("{Type,nq}, {Header,nq}")]
        private class TableHeaderInfo
        {
            public string Header { get; set; }
            public TableHeaderType Type { get; set; } = TableHeaderType.Unknown;
        }

        private static Dictionary<int, TableHeaderInfo> GetTableHeaders(IElement table)
        {
            Dictionary<int, TableHeaderInfo> tableHeaders = new Dictionary<int, TableHeaderInfo>();

            IHtmlCollection<IElement> headers = table.QuerySelector("th")?.ParentElement?.QuerySelectorAll("th");

            if (headers == null)
            {
                // snif directory listing
                headers = table.QuerySelector(".snHeading")?.QuerySelectorAll("td");
            }

            if (headers == null || headers.Length == 0)
            {
                headers = table.QuerySelectorAll("thead td, thead th");
            }

            if (headers == null || headers.Length == 0)
            {
                headers = table.QuerySelectorAll("> tr:nth-child(1) > td");
            }

            if (headers?.Any() == true)
            {
                int headerIndex = 1;

                foreach (IElement header in headers)
                {
                    if (header.QuerySelector("table") != null)
                    {
                        continue;
                    }

                    string headerName = header.TextContent.Trim();

                    TableHeaderInfo tableHeaderInfo = new TableHeaderInfo
                    {
                        Header = headerName
                    };

                    headerName = headerName.ToLower();

                    if (headerName == "last modified" || headerName == "modified" || headerName.Contains("date") || headerName.Contains("last modification"))
                    {
                        tableHeaderInfo.Type = TableHeaderType.Modified;
                    }

                    if (headerName == "type")
                    {
                        tableHeaderInfo.Type = TableHeaderType.Type;
                    }

                    if (headerName == "size" || headerName.Contains("file size") || headerName.Contains("taille"))
                    {
                        tableHeaderInfo.Type = TableHeaderType.FileSize;
                    }

                    if (headerName == "description")
                    {
                        tableHeaderInfo.Type = TableHeaderType.Description;
                    }

                    // Check this as last one because of generic 'file' in it..
                    if (tableHeaderInfo.Type == TableHeaderType.Unknown &&
                        (headerName == "file" ||
                        headerName == "name" ||
                        headerName.Contains("file name") ||
                        headerName.Contains("filename") ||
                        headerName == "directory" ||
                        headerName.Contains("nom")))
                    {
                        tableHeaderInfo.Type = TableHeaderType.FileName;
                    }

                    tableHeaders.Add(headerIndex, tableHeaderInfo);

                    headerIndex++;
                }

                // Dynamically guess column types
                if (tableHeaders.All(th => th.Value.Type == TableHeaderType.Unknown))
                {
                    tableHeaders.Clear();

                    List<int> fileNameColumnIndex = new List<int>();
                    List<int> dateColumnIndex = new List<int>();
                    List<int> fileSizeColumnIndex = new List<int>();
                    List<int> typeColumnIndex = new List<int>();

                    foreach (IElement tableRow in table.QuerySelectorAll("tr"))
                    {
                        foreach (IElement tableColumn in tableRow.QuerySelectorAll("td"))
                        {
                            if (tableColumn.QuerySelector("a") != null)
                            {
                                fileNameColumnIndex.Add(tableColumn.Index());
                            }

                            if (DateTime.TryParse(tableColumn.TextContent, out DateTime parsedDateTime) && parsedDateTime != DateTime.MinValue)
                            {
                                dateColumnIndex.Add(tableColumn.Index());
                            }

                            if (FileSizeHelper.ParseFileSize(tableColumn.TextContent, default, false) > -1)
                            {
                                fileSizeColumnIndex.Add(tableColumn.Index());
                            }

                            if (tableColumn.QuerySelector("img") != null)
                            {
                                typeColumnIndex.Add(tableColumn.Index());
                            }
                        }
                    }

                    if (fileNameColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(fileNameColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new TableHeaderInfo { Type = TableHeaderType.FileName });
                        }
                    }

                    if (dateColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(dateColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new TableHeaderInfo { Type = TableHeaderType.Modified });
                        }
                    }

                    if (fileSizeColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(fileSizeColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new TableHeaderInfo { Type = TableHeaderType.FileSize });
                        }
                    }

                    if (typeColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(typeColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new TableHeaderInfo { Type = TableHeaderType.Type });
                        }
                    }
                }

                return tableHeaders;
            }

            return tableHeaders;
        }

        private static bool IsValidLink(IElement link)
        {
            string linkHref = link.Attributes["href"].Value;

            return
                linkHref != "/" &&
                linkHref != ".." &&
                linkHref != "../" &&
                linkHref != "./." &&
                linkHref != "./.." &&
                linkHref != "#" &&
                (link as IHtmlAnchorElement)?.Title != ".." &&
                link.TextContent.Trim() != ".." &&
                link.TextContent.Trim() != "." &&
                !linkHref.ToLower().StartsWith("javascript") &&
                !linkHref.ToLower().StartsWith("mailto:") &&
                link.TextContent.ToLower() != "parent directory" &&
                link.TextContent.Trim() != "Name" &&
                (Path.GetFileName(linkHref) != "DirectoryList.asp" || !string.IsNullOrWhiteSpace(link.TextContent));
        }
    }
}
