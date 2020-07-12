using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.BlitzfilesTech;
using OpenDirectoryDownloader.Site.GoIndex;
using OpenDirectoryDownloader.Site.GoIndex.Bhadoo;
using OpenDirectoryDownloader.Site.GoIndex.GdIndex;
using OpenDirectoryDownloader.Site.GoIndex.Go2Index;
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
                Name = WebUtility.UrlDecode(Path.GetDirectoryName(new Uri(baseUrl).Segments.LastOrDefault())) ?? "ROOT"
            };

            try
            {
                IHtmlDocument htmlDocument = await HtmlParser.ParseDocumentAsync(html);

                if (webDirectory.Uri.Host == "ipfs.io" || webDirectory.Uri.Host == "gateway.ipfs.io")
                {
                    return ParseIpfsDirectoryListing(baseUrl, parsedWebDirectory, htmlDocument);
                }

                if (webDirectory.Uri.Host == Constants.BlitzfilesTechDomain)
                {
                    return await BlitzfilesTechParser.ParseIndex(httpClient, webDirectory);
                }

                if (htmlDocument.QuerySelector("script[src*=\"goindex-theme-acrou\"]") != null)
                {
                    return await Go2IndexParser.ParseIndex(httpClient, webDirectory);
                }

                if (htmlDocument.QuerySelector("script[src*=\"Bhadoo-Drive-Index\"]") != null || htmlDocument.QuerySelector("script[src*=\"/AjmalShajahan97/goindex\"]") != null)
                {
                    return await BhadooIndexParser.ParseIndex(httpClient, webDirectory);
                }

                if (htmlDocument.QuerySelector("script[src*=\"gdindex\"]") != null)
                {
                    return await GdIndexParser.ParseIndex(httpClient, webDirectory, html);
                }

                if (htmlDocument.QuerySelector("script[src*=\"/go2index/\"]") != null)
                {
                    return await Go2IndexParser.ParseIndex(httpClient, webDirectory);
                }

                // goindex, goindex-drive, goindex-backup
                if (htmlDocument.QuerySelector("script[src*=\"goindex\"]") != null)
                {
                    return await GoIndexParser.ParseIndex(httpClient, webDirectory);
                }

                htmlDocument.QuerySelectorAll("#sidebar").ToList().ForEach(e => e.Remove());
                htmlDocument.QuerySelectorAll("nav").ToList().ForEach(e => e.Remove());

                // The order of the checks are very important!

                IHtmlCollection<IElement> directoryListingDotComlistItems = htmlDocument.QuerySelectorAll("#directory-listing li, .directory-listing li");

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
                    bool containsFileSizeClass = htmlDocument.QuerySelector(".fileSize") != null;
                    return ParseTablesDirectoryListing(baseUrl, parsedWebDirectory, tables, containsFileSizeClass);
                }

                IHtmlCollection<IElement> materialDesignListItems = htmlDocument.QuerySelectorAll("ul.mdui-list li");

                if (materialDesignListItems.Any())
                {
                    return ParseMaterialDesignListItemsDirectoryListing(baseUrl, parsedWebDirectory, materialDesignListItems);
                }

                if (htmlDocument.Title.EndsWith("Directory Lister") && htmlDocument.QuerySelectorAll("#content ul#file-list li").Length == 2)
                {
                    return ParseDirectoryListerDirectoryListing(baseUrl, parsedWebDirectory, htmlDocument);
                }

                IHtmlCollection<IElement> listItems = htmlDocument.QuerySelectorAll(".list-group li");

                if (listItems.Any())
                {
                    bool containsFileSizeClass = htmlDocument.QuerySelector(".fileSize") != null;
                    WebDirectory result = ParseListItemsDirectoryListing(baseUrl, parsedWebDirectory, listItems, containsFileSizeClass);

                    if (result.ParsedSuccesfully || result.Error)
                    {
                        return result;
                    }
                }

                listItems = htmlDocument.QuerySelectorAll("ul li");

                if (listItems.Any())
                {
                    bool containsFileSizeClass = htmlDocument.QuerySelector(".fileSize") != null;
                    WebDirectory result = ParseListItemsDirectoryListing(baseUrl, parsedWebDirectory, listItems, containsFileSizeClass);

                    if (result.ParsedSuccesfully || result.Error)
                    {
                        return result;
                    }
                }

                // Latest fallback
                IHtmlCollection<IElement> links = htmlDocument.QuerySelectorAll("a");

                if (links.Any())
                {
                    bool containsFileSizeClass = htmlDocument.QuerySelector(".fileSize") != null;
                    parsedWebDirectory = ParseLinksDirectoryListing(baseUrl, parsedWebDirectory, links, containsFileSizeClass);
                }

                parsedWebDirectory = await ParseDirectoryListingModel01(baseUrl, parsedWebDirectory, htmlDocument, httpClient);

                CheckParsedResults(parsedWebDirectory);

                return parsedWebDirectory;
            }
            catch (FriendlyException ex)
            {
                Logger.Error(ex.Message);

                parsedWebDirectory.Error = true;
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
                        parsedWebDirectory.FinishTime = newWebDirectory.FinishTime;
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
                Name = directoryListingModel.Name
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

            Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);

            KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
            int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

            KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
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
            string urlFromBreadcrumbs = Uri.EscapeUriString(string.Join("/", htmlDocument.QuerySelectorAll(".breadcrumbs_main .breadcrumb").Where(b => !b.ClassList.Contains("smaller")).Select(b => b.TextContent.Trim())) + "/");

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

                Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);

                KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
                int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

                KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
                int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

                foreach (IElement tableRow in pureTableRows)
                {
                    string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();

                    bool isFile = !tableRow.ClassList.Contains("dir");

                    IElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a");
                    string linkHref = link.TextContent;

                    if (IsValidLink(link))
                    {
                        Uri uri = new Uri(new Uri(baseUrl), Uri.EscapeUriString(linkHref));
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
                                Name = link.TextContent
                            });
                        }
                        else
                        {
                            parsedWebDirectory.Files.Add(new WebFile
                            {
                                Url = fullUrl,
                                FileName = link.TextContent,
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

            Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);

            KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
            int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

            KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
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

        private static WebDirectory ParseTablesDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> tables, bool containsFileSizeClass)
        {
            // Dirty solution..
            bool hasSeperateDirectoryAndFilesTables = false;

            List<WebDirectory> results = new List<WebDirectory>();

            foreach (IElement table in tables)
            {
                WebDirectory webDirectoryCopy = JsonConvert.DeserializeObject<WebDirectory>(JsonConvert.SerializeObject(parsedWebDirectory));

                Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);
                webDirectoryCopy.HeaderCount = tableHeaders.Count(th => th.Value.Type != HeaderType.Unknown);

                KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
                int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

                KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
                int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

                KeyValuePair<int, HeaderInfo> descriptionHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.Description);
                int descriptionHeaderColumnIndex = descriptionHeader.Value != null ? descriptionHeader.Key : 0;

                if (fileSizeHeaderColumnIndex == 0 && nameHeaderColumnIndex == 0)
                {
                    if (table.QuerySelector("a") != null)
                    {
                        webDirectoryCopy = ParseLinksDirectoryListing(baseUrl, webDirectoryCopy, table.QuerySelectorAll("a"), containsFileSizeClass);
                    }
                }
                else
                {
                    webDirectoryCopy.ParsedSuccesfully = true;

                    foreach (IElement tableRow in table.QuerySelectorAll("tbody tr"))
                    {
                        if (tableRow.QuerySelector("img[alt=\"[ICO]\"]") == null &&
                            tableRow.QuerySelector("img[alt=\"[PARENTDIR]\"]") == null &&
                            tableRow.QuerySelector("a") != null &&
                            tableRow.QuerySelector("th") == null &&
                            !tableRow.ClassList.Contains("snHeading") &&
                            tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})") != null &&
                            !tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})").TextContent.ToLower().Contains("parent directory") &&
                            tableRow.QuerySelector("table") == null)
                        {
                            bool addedEntry = false;

                            foreach (IElement link in tableRow.QuerySelectorAll("a"))
                            {
                                if (!addedEntry && IsValidLink(link))
                                {
                                    addedEntry = true;

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
                                            if (Library.IsBase64String(urlEncodingParser["folder"]))
                                            {
                                                directoryName = Encoding.UTF8.GetString(Convert.FromBase64String(urlEncodingParser["folder"]));
                                            }
                                            else
                                            {
                                                directoryName = link.TextContent.Trim();
                                            }
                                        }

                                        webDirectoryCopy.Subdirectories.Add(new WebDirectory(webDirectoryCopy)
                                        {
                                            Parser = "ParseTablesDirectoryListing",
                                            Url = fullUrl,
                                            Name = WebUtility.UrlDecode(directoryName),
                                            Description = description
                                        });
                                    }
                                    else
                                    {
                                        webDirectoryCopy.Parser = "ParseTablesDirectoryListing";

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

                                        webDirectoryCopy.Files.Add(new WebFile
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
                    }
                }

                results.Add(webDirectoryCopy);
            }

            if (!hasSeperateDirectoryAndFilesTables)
            {
                parsedWebDirectory = results.Where(r => (r.ParsedSuccesfully || r.Error) && (r.Files.Count > 0 || r.Subdirectories.Count > 0)).OrderByDescending(r => r.HeaderCount).ThenByDescending(r => r.TotalDirectoriesIncludingUnfinished + r.TotalFiles).FirstOrDefault() ?? parsedWebDirectory;
            }
            else
            {
                parsedWebDirectory.Subdirectories = new ConcurrentList<WebDirectory>(results.SelectMany(r => r.Subdirectories));
                parsedWebDirectory.Files = results.SelectMany(r => r.Files).ToList();
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
            Match match = Regex.Match(line, @"<a.*<\/a>\s*(?<DateTime>\d+-\w+-\d+\s\d+:\d{0,2}|-)\s*(?<FileSize>\S+\s?\S*)\s*\S*");

            if (match.Success)
            {
                string fileSizeGroup = match.Groups["FileSize"].Value.Trim();

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);
                IElement link = parsedLine.QuerySelector("a");
                string linkHref = link.Attributes["href"].Value;

                Uri uri = new Uri(new Uri(baseUrl), linkHref);
                string fullUrl = uri.ToString();

                if (IsValidLink(link))
                {
                    bool isFile = long.TryParse(fileSizeGroup, out long fileSize);

                    if (!isFile && IsFileSize(fileSizeGroup))
                    {
                        fileSize = FileSizeHelper.ParseFileSize(fileSizeGroup);
                        isFile = fileSize > 0;
                    }

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

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser7 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"(?i)(?<FileMode>[d-]r[-w][x-])\s*\d*\s*(?<FileSize>-?\d*)\s*(\S{3}\s*\d*\s*(?:\d*:\d*(:\d*)?|\d*\.?))\s*(<a.*<\/a>\/?)");

            if (match.Success)
            {
                bool isFile = !match.Groups["FileMode"].Value.ToLower().StartsWith("d");

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("a") != null)
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
                                Parser = "RegexParser7",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()))
                            });
                        }
                        else
                        {
                            try
                            {
                                string fileSize = match.Groups["FileSize"].Value;

                                if (fileSize.StartsWith("-"))
                                {
                                    // If filesize is negative, it will be 4GB minus the amount of bytes (without the - sign),
                                    // but this will only work for when it is between 2 and 4 GB, so skip it
                                    fileSize = string.Empty;
                                }

                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(fileSize)
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser7");
                            }
                        }
                    }
                }
            }

            return match.Success;
        };

        private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser8 = async (webDirectory, baseUrl, line) =>
        {
            Match match = Regex.Match(line, @"<a.*<\/a>\s*\/?(?<FileSize>\S+)?");

            if (match.Success)
            {
                bool isFile = !string.IsNullOrWhiteSpace(match.Groups["FileSize"].Value);

                if (match.Groups["FileSize"].Value.Contains("<"))
                {
                    return false;
                }

                IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

                if (parsedLine.QuerySelector("a") != null)
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
                                Parser = "RegexParser8",
                                Url = fullUrl,
                                Name = WebUtility.UrlDecode(linkHref)
                            });
                        }
                        else
                        {
                            try
                            {
                                string fileSize = match.Groups["FileSize"].Value;

                                webDirectory.Files.Add(new WebFile
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                    FileSize = FileSizeHelper.ParseFileSize(fileSize)
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error parsing with RegexParser8");
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
                RegexParser7,
                RegexParser8,
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

                if (!div.Children.Any())
                {
                    switch (GetHeaderInfo(div).Type)
                    {
                        case HeaderType.FileName:
                            nameIndex = columnIndex;
                            break;
                        case HeaderType.FileSize:
                            sizeIndex = columnIndex;
                            break;
                        case HeaderType.Modified:
                            modifiedIndex = columnIndex;
                            break;
                        default:
                            break;
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

            // Format 3
            //<li class="mdui-list-item th">
            //  <div class="mdui-col-xs-12 mdui-col-sm-7">文件</div>
            //  <div class="mdui-col-sm-3 mdui-text-right">修改时间</div>
            //  <div class="mdui-col-sm-2 mdui-text-right">大小</div>
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

        private static WebDirectory ParseDirectoryListerDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument)
        {
            parsedWebDirectory.Parser = "ParseDirectoryListerDirectoryListing";
            List<HeaderInfo> tableHeaderInfos = new List<HeaderInfo>();

            IHtmlCollection<IElement> headerDivs = htmlDocument.QuerySelectorAll("#content > div > div > div");

            foreach (IElement headerDiv in headerDivs)
            {
                tableHeaderInfos.Add(GetHeaderInfo(headerDiv));
            }

            IHtmlCollection<IElement> entries = htmlDocument.QuerySelectorAll("#content ul#file-list li").Last().QuerySelectorAll("a");

            foreach (IElement entry in entries)
            {
                bool isFile = !entry.QuerySelector("i").ClassList.Contains("fa-folder");

                //IElement link = entry.QuerySelector("a");
                string linkHref = entry.Attributes["href"].Value;

                if (IsValidLink(entry))
                {
                    Uri uri = new Uri(new Uri(baseUrl), linkHref);
                    string fullUrl = uri.ToString();
                    UrlEncodingParser urlEncodingParser = new UrlEncodingParser(fullUrl);

                    if (!isFile)
                    {
                        parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
                        {
                            Parser = "ParseDirectoryListerDirectoryListing",
                            Url = fullUrl,
                            Name = WebUtility.UrlDecode(urlEncodingParser["dir"].Split("/").Last()),
                        });
                    }
                    else
                    {
                        try
                        {
                            List<IElement> divs = entry.QuerySelectorAll("div > div").ToList();
                            // Remove file info 'column'
                            divs.RemoveAt(tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileName) + 1);
                            string fileSize = entry.QuerySelectorAll("div > div").Skip(2).ToList()[tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileSize)].TextContent;

                            parsedWebDirectory.Files.Add(new WebFile
                            {
                                Url = fullUrl,
                                FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
                                FileSize = FileSizeHelper.ParseFileSize(fileSize),
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error parsing with ParseDirectoryListerDirectoryListing");
                        }
                    }
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseListItemsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool containsFileSizeClass)
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
                    ProcessLink(baseUrl, parsedWebDirectory, link, "ParseListItemsDirectoryListing", containsFileSizeClass);
                }
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static WebDirectory ParseLinksDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> links, bool containsFileSizeClass)
        {
            foreach (IElement link in links)
            {
                ProcessLink(baseUrl, parsedWebDirectory, link, "ParseLinksDirectoryListing", containsFileSizeClass);
            }

            CheckParsedResults(parsedWebDirectory);

            return parsedWebDirectory;
        }

        private static void ProcessLink(string baseUrl, WebDirectory parsedWebDirectory, IElement link, string parser, bool containsFileSizeClass)
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

                    if (uri.Segments.Length == 1 && uri.Segments.Last() == "/" && urlEncodingParser["dir"] == null && urlEncodingParser["path"] == null)
                    {
                        return;
                    }

                    if (baseUrl == StripUrl(fullUrl))
                    {
                        return;
                    }

                    parsedWebDirectory.ParsedSuccesfully = true;

                    bool directoryListAsp = Path.GetFileName(fullUrl) == "DirectoryList.asp" || fullUrl.Contains("DirectoryList.asp");
                    bool dirParam = urlEncodingParser["dir"] != null || urlEncodingParser["path"] != null;

                    if (!string.IsNullOrWhiteSpace(Path.GetExtension(fullUrl)) && !directoryListAsp && !dirParam)
                    {
                        parsedWebDirectory.Parser = parser;

                        long fileSize = FileSizeHelper.ParseFileSize(containsFileSizeClass ? link.ParentElement?.QuerySelector(".fileSize")?.TextContent : null);

                        string fileName = Path.GetFileName(WebUtility.UrlDecode(linkHref));
                        urlEncodingParser = new UrlEncodingParser(fileName);

                        // Clear token
                        if (urlEncodingParser["token"] != null)
                        {
                            urlEncodingParser.Remove("token");
                            fileName = urlEncodingParser.ToString();
                        }

                        parsedWebDirectory.Files.Add(new WebFile
                        {
                            Url = fullUrl,
                            FileName = fileName,
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
                                Name = urlEncodingParser["dir"] ?? WebUtility.UrlDecode(new Uri(parsedWebDirectory.Uri, urlEncodingParser["path"]).Segments.Last()).TrimEnd(new char[] { '/' })
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

        public enum HeaderType
        {
            Unknown,
            FileName,
            FileSize,
            Modified,
            Description,
            Type
        }

        [DebuggerDisplay("{Type,nq}, {Header,nq}")]
        private class HeaderInfo
        {
            public string Header { get; set; }
            public HeaderType Type { get; set; } = HeaderType.Unknown;
        }

        private static Dictionary<int, HeaderInfo> GetTableHeaders(IElement table)
        {
            Dictionary<int, HeaderInfo> tableHeaders = new Dictionary<int, HeaderInfo>();

            IHtmlCollection<IElement> headers = table.QuerySelector("th")?.ParentElement?.QuerySelectorAll("th");

            bool removeFirstRow = false;

            if (headers != null && headers.First().HasAttribute("colspan"))
            {
                headers = null;
            }

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
                headers = table.QuerySelectorAll("tr:nth-child(1) > th");
            }

            if (headers == null || headers.Length == 0)
            {
                headers = table.QuerySelectorAll("tr:nth-child(1) > td");

                if (headers?.Length > 0)
                {
                    removeFirstRow = true;
                }
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

                    HeaderInfo tableHeaderInfo = GetHeaderInfo(header);

                    tableHeaders.Add(headerIndex, tableHeaderInfo);

                    if (header.HasAttribute("colspan"))
                    {
                        headerIndex += int.Parse(header.GetAttribute("colspan")) - 1;
                    }

                    headerIndex++;
                }

                // Dynamically guess column types
                if (tableHeaders.All(th => th.Value.Type == HeaderType.Unknown))
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
                            tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.FileName });
                        }
                    }

                    if (dateColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(dateColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.Modified });
                        }
                    }

                    if (fileSizeColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(fileSizeColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.FileSize });
                        }
                    }

                    if (typeColumnIndex.Any())
                    {
                        int columnIndex = ((int)Math.Round(typeColumnIndex.Average())) + 1;

                        if (!tableHeaders.ContainsKey(columnIndex))
                        {
                            tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.Type });
                        }
                    }
                }
                else
                {
                    if (tableHeaders.Any(th => th.Value.Type == HeaderType.FileName) && tableHeaders.Any(th => th.Value.Type == HeaderType.FileSize) && removeFirstRow)
                    {
                        table.QuerySelector("tr:nth-child(1)").Remove();
                    }
                }

                return tableHeaders;
            }

            return tableHeaders;
        }

        private static HeaderInfo GetHeaderInfo(IElement header)
        {
            string headerName = header.TextContent.Trim();

            HeaderInfo headerInfo = new HeaderInfo
            {
                Header = headerName
            };

            headerName = headerName.ToLower();

            headerName = Regex.Replace(headerName, @"[^a-zA-Z0-9\s一-龥]", string.Empty);

            if (headerName == "last modified" || headerName == "modified" || headerName.Contains("date") || headerName.Contains("last modification") || headerName.Contains("time") || headerName.Contains("修改时间"))
            {
                headerInfo.Type = HeaderType.Modified;
            }

            if (headerName == "type")
            {
                headerInfo.Type = HeaderType.Type;
            }

            if (headerName == "size" || headerName.Contains("file size") || headerName.Contains("filesize") || headerName.Contains("taille") || headerName.Contains("大小"))
            {
                headerInfo.Type = HeaderType.FileSize;
            }

            if (headerName == "description")
            {
                headerInfo.Type = HeaderType.Description;
            }

            // Check this as last one because of generic 'file' in it..
            if (headerInfo.Type == HeaderType.Unknown &&
                (headerName == "file" ||
                headerName == "name" ||
                headerName.Contains("file name") ||
                headerName.Contains("filename") ||
                headerName == "directory" ||
                headerName.Contains("link") ||
                headerName.Contains("nom") ||
                headerName.Contains("文件")))
            {
                headerInfo.Type = HeaderType.FileName;
            }

            return headerInfo;
        }

        private static bool IsValidLink(IElement link)
        {
            string linkHref = link.Attributes["href"]?.Value;

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
                linkHref?.ToLower().StartsWith("javascript") == false &&
                linkHref?.ToLower().StartsWith("mailto:") == false &&
                link.TextContent.ToLower() != "parent directory" &&
                link.TextContent.ToLower() != "[to parent directory]" &&
                link.TextContent.Trim() != "Name" &&
                linkHref?.Contains("&expand") == false &&
                (!new Regex(@"\?[NMSD]=?[AD]").IsMatch(linkHref) || linkHref.StartsWith("DirectoryList.asp")) &&
                (Path.GetFileName(linkHref) != "DirectoryList.asp" || !string.IsNullOrWhiteSpace(link.TextContent));
        }
    }
}
