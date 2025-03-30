using Acornima;
using Acornima.Ast;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Newtonsoft.Json;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.BlitzfilesTech;
using OpenDirectoryDownloader.Site.Copyparty;
using OpenDirectoryDownloader.Site.Dropbox;
using OpenDirectoryDownloader.Site.FileBrowser;
using OpenDirectoryDownloader.Site.GDIndex;
using OpenDirectoryDownloader.Site.GDIndex.Bhadoo;
using OpenDirectoryDownloader.Site.GDIndex.GdIndex;
using OpenDirectoryDownloader.Site.GDIndex.Go2Index;
using OpenDirectoryDownloader.Site.GDIndex.GoIndex;
using OpenDirectoryDownloader.Site.GitHub;
using OpenDirectoryDownloader.Site.GoFileIO;
using OpenDirectoryDownloader.Site.HFS;
using OpenDirectoryDownloader.Site.Mediafire;
using OpenDirectoryDownloader.Site.PCloud;
using OpenDirectoryDownloader.Site.Pixeldrain;
using PuppeteerSharp;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader;

public static partial class DirectoryParser
{
	private static readonly HtmlParser HtmlParser = new();

	private static readonly SemaphoreSlim SemaphoreSlimBrowser = new(1, 1);

	private static readonly char[] trimChars = ['/'];

	/// <summary>
	/// Parses Html to a WebDirectory object containing the current directory index
	/// </summary>
	/// <param name="baseUrl">Base url</param>
	/// <param name="html">Html to parse</param>
	/// <returns>WebDirectory object containing current directory index</returns>
	public static async Task<WebDirectory> ParseHtml(WebDirectory webDirectory, string html, HttpClient httpClient = null, SocketsHttpHandler socketsHttpHandler = null, HttpResponseMessage httpResponseMessage = null, bool checkParents = true)
	{
		string baseUrl = webDirectory.Url;

		if (!baseUrl.EndsWith('/') && string.IsNullOrWhiteSpace(webDirectory.Uri.Query) && string.IsNullOrWhiteSpace(Path.GetExtension(baseUrl)))
		{
			baseUrl += '/';
		}

		WebDirectory parsedWebDirectory = new(webDirectory.ParentDirectory)
		{
			Url = baseUrl,
			Name = WebUtility.UrlDecode(Path.GetDirectoryName(new Uri(baseUrl).Segments.LastOrDefault())) ?? Constants.Root
		};

		try
		{
            string webDirectoryUriHost = webDirectory.Uri.Host.ToLower();

            if (webDirectoryUriHost == Constants.BlitzfilesTechDomain)
            {
                return await BlitzfilesTechParser.ParseIndex(httpClient, webDirectory);
            }

            if (webDirectoryUriHost == Constants.DropboxDomain)
            {
                return await DropboxParser.ParseIndex(httpClient, webDirectory, html, httpResponseMessage);
            }

            if (webDirectoryUriHost == Constants.GitHubDomain || webDirectoryUriHost == Constants.GitHubApiDomain)
            {
                return await GitHubParser.ParseIndex(httpClient, webDirectory);
            }

            if (webDirectoryUriHost == Constants.GoFileIoDomain)
            {
                return await GoFileIOParser.ParseIndex(httpClient, webDirectory);
            }

            if (webDirectoryUriHost == Constants.MediafireDomain)
            {
                return await MediafireParser.ParseIndex(httpClient, webDirectory);
            }

            if (webDirectoryUriHost == Constants.PCloudDomain1 || webDirectoryUriHost == Constants.PCloudDomain2)
            {
				if (html.Contains("directLinkData"))
				{
					return await PCloudParser.ParseIndex(httpClient, webDirectory, html);
				}
            }

            if (webDirectoryUriHost == Constants.PixeldrainDomain)
            {
                return await PixeldrainParser.ParseIndex(httpClient, webDirectory, html);
            }

            if (httpResponseMessage?.Headers.Server?.ToString()?.StartsWith("hfs", StringComparison.InvariantCultureIgnoreCase) == true)
            {
                string httpHeaderServer = httpResponseMessage?.Headers.Server?.ToString();
                return await HfsParser.ParseIndex(httpClient, webDirectory, html, httpHeaderServer);
            }

			IHtmlDocument htmlDocument = await HtmlParser.ParseDocumentAsync(html);

			if (webDirectory.Uri.Host == "ipfs.io" || webDirectory.Uri.Host == "gateway.ipfs.io")
			{
				return ParseIpfsDirectoryListing(baseUrl, parsedWebDirectory, htmlDocument, checkParents);
			}

			// https://github.com/filebrowser/filebrowser
			if (htmlDocument.Title == "File Browser")
			{
				Regex scriptRegex = new(@"app\..*\.js");

				if (htmlDocument.Scripts.Any(s => s.Source is not null && scriptRegex.IsMatch(s.Source)))
				{
					Regex baseUrlRegex = new(@"""BaseURL"":""(?<BaseUrl>.*?)"",");

					Match baseUrlRegexMatch = baseUrlRegex.Match(html);

					if (baseUrlRegexMatch.Success)
					{
						return await FileBrowserParser.ParseIndex(baseUrl, httpClient, parsedWebDirectory, htmlDocument, html);
					}
				}
			}

			if (httpClient is not null && !OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.GoogleDriveIndexType))
			{
				string googleDriveIndexType = null;

				foreach (IHtmlScriptElement script in htmlDocument.Scripts.Where(s => s.Source is not null))
				{
					googleDriveIndexType = GoogleDriveIndexMapping.GetGoogleDriveIndexType(script.Source);

					if (googleDriveIndexType is null && script.Source.Contains("app.min.js", StringComparison.InvariantCultureIgnoreCase))
					{
						Program.Logger.Warning("Checking/downloading javascript for sourcemaps: {scriptUrl}", script.Source);
						string sourceMapUrl = await Library.GetSourceMapUrlFromJavaScriptAsync(httpClient, script.Source);

						if (!string.IsNullOrWhiteSpace(sourceMapUrl))
						{
							string fullSourceMapUrl = new Uri(new Uri(script.Source), sourceMapUrl).ToString();
							Program.Logger.Warning("Checking/downloading sourcemap for known Google Drive index: {sourceMapUrl}", fullSourceMapUrl);

							IAsyncEnumerable<string> sources = Library.GetSourcesFromSourceMapAsync(httpClient, fullSourceMapUrl);

							await foreach (string source in sources)
							{
								googleDriveIndexType = GoogleDriveIndexMapping.GetGoogleDriveIndexType(source);

								if (googleDriveIndexType is not null)
								{
									break;
								}
							}
						}
					}

					if (googleDriveIndexType is null && script.Source.Contains("app.js", StringComparison.InvariantCultureIgnoreCase))
					{
						string scriptUrl = script.Source;

						if (Uri.IsWellFormedUriString(scriptUrl, UriKind.Relative))
						{
							scriptUrl = new Uri(OpenDirectoryIndexer.Session.Root.Uri, scriptUrl).ToString();
						}

						string appJsSource = await httpClient.GetStringAsync(scriptUrl);

						Parser javaScriptParser = new();
						Script program = javaScriptParser.ParseScript(appJsSource);
						IEnumerable<FunctionDeclaration> javaScriptFunctions = program.ChildNodes.OfType<FunctionDeclaration>();
						FunctionDeclaration gdidecodeFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "gdidecode"));

						if (gdidecodeFunctionDeclaration is not null)
						{
							googleDriveIndexType = GoogleDriveIndexMapping.BhadooIndex;
							break;
						}
					}

					if (googleDriveIndexType is not null)
					{
						break;
					}
				}

				if (googleDriveIndexType is not null)
				{
					OpenDirectoryIndexer.Session.Parameters[Constants.GoogleDriveIndexType] = googleDriveIndexType;

					if (OpenDirectoryIndexer.Session.MaxThreads != 1)
					{
						Program.Logger.Warning("Reduce threads to 1 because of Google Drive index");
						OpenDirectoryIndexer.Session.MaxThreads = 1;
					}
				}
			}

			if (OpenDirectoryIndexer.Session is not null && OpenDirectoryIndexer.Session.Parameters.TryGetValue(Constants.GoogleDriveIndexType, out string value))
			{
				string googleDriveIndexType = value;

				switch (googleDriveIndexType)
				{
					case GoogleDriveIndexMapping.BhadooIndex:
						return await BhadooIndexParser.ParseIndex(htmlDocument, httpClient, webDirectory);
					case GoogleDriveIndexMapping.GoIndex:
						return await GoIndexParser.ParseIndex(httpClient, webDirectory);
					case GoogleDriveIndexMapping.Go2Index:
						return await Go2IndexParser.ParseIndex(httpClient, webDirectory);
					case GoogleDriveIndexMapping.GdIndex:
						return await GdIndexParser.ParseIndex(httpClient, webDirectory, html);
				}
			}

			htmlDocument.QuerySelectorAll("#sidebar").ToList().ForEach(e => e.Remove());
			htmlDocument.QuerySelectorAll("nav").ToList().ForEach(e => e.Remove());

			// The order of the checks is very important!

			if (htmlDocument.Title.StartsWith("HFS /"))
			{
				// HFS (legacy) up to 2.3x
				// document.querySelectorAll('#files tr')
				IHtmlCollection<IElement> hfsTable = htmlDocument.QuerySelectorAll("table#files");

				if (hfsTable.Length != 0)
				{
					return ParseTablesDirectoryListing(baseUrl, parsedWebDirectory, hfsTable, checkParents);
				}

				if (htmlDocument.QuerySelector("div#files") == null)
				{
					return parsedWebDirectory;
				}

				// HFS (legacy) 2.4+
				// This is already handled by normal parsers
			}

			if (htmlDocument.Title.StartsWith("pCloud - "))
			{
				if (html.Contains("directLinkData"))
				{
					return await PCloudParser.ParseIndex(httpClient, webDirectory, html);
				}
			}

			IHtmlCollection<IElement> directoryListingDotComlistItems = htmlDocument.QuerySelectorAll("#directory-listing li, .directory-listing li");

			if (directoryListingDotComlistItems.Length != 0)
			{
				return ParseDirectoryListingDoctComDirectoryListing(baseUrl, parsedWebDirectory, directoryListingDotComlistItems, checkParents);
			}

			IHtmlCollection<IElement> h5aiTableRows = htmlDocument.QuerySelectorAll("#fallback table tr");

			if (h5aiTableRows.Length != 0)
			{
				return ParseH5aiDirectoryListing(baseUrl, parsedWebDirectory, h5aiTableRows, checkParents);
			}

			// Snif directory listing
			// http://web.archive.org/web/20140724000351/http://bitfolge.de/index.php?option=com_content&view=article&id=66:snif-simple-and-nice-index-file&catid=38:eigene&Itemid=59
			IHtmlCollection<IElement> snifTableRows = htmlDocument.QuerySelectorAll("table.snif tr");

			if (snifTableRows.Length != 0)
			{
				return ParseSnifDirectoryListing(baseUrl, parsedWebDirectory, snifTableRows, checkParents);
			}

			// Godir - https://gitlab.com/Montessquio/godir
			IHtmlCollection<IElement> pureTableRows = htmlDocument.QuerySelectorAll("table.listing-table tbody tr");

			if (pureTableRows.Length != 0)
			{
				return ParsePureDirectoryListing(ref baseUrl, parsedWebDirectory, htmlDocument, pureTableRows, checkParents);
			}

			// Remove it after ParsePureDirectoryListing (.breadcrumb is used in it)
			htmlDocument.QuerySelectorAll(".breadcrumb").ToList().ForEach(e => e.Remove());

			// Custom directory listing
			IHtmlCollection<IElement> divElements = htmlDocument.QuerySelectorAll("div#listing div");

			if (divElements.Length != 0)
			{
				return ParseCustomDivListing(ref baseUrl, parsedWebDirectory, htmlDocument, divElements, checkParents);
			}

			// Custom directory listing 2
			divElements = htmlDocument.QuerySelectorAll("div#filelist .tb-row.folder,div#filelist .tb-row.afile");

			if (divElements.Length != 0)
			{
				return ParseCustomDivListing2(ref baseUrl, parsedWebDirectory, htmlDocument, divElements, checkParents);
			}

			// HFS
			divElements = htmlDocument.QuerySelectorAll("div#files .item");

			if (divElements.Length != 0)
			{
				return ParseHfsListing(ref baseUrl, parsedWebDirectory, htmlDocument, divElements, checkParents);
			}

			// copyparty
			if (htmlDocument.QuerySelector("#op_bup #u2err") is not null)
			{
				return await ParseCopypartyListingAsync(baseUrl, httpClient, parsedWebDirectory, htmlDocument, html);
			}

			IHtmlCollection<IElement> pres = htmlDocument.QuerySelectorAll("pre");

			if (pres.Length != 0)
			{
				WebDirectory result = await ParsePreDirectoryListing(baseUrl, parsedWebDirectory, pres, checkParents);

				if (result.Files.Count != 0 || result.Subdirectories.Count != 0 || result.Error)
				{
					return result;
				}
			}

			WebDirectory parsedJavaScriptDrawn = ParseJavaScriptDrawn(baseUrl, parsedWebDirectory, html);

			if (parsedJavaScriptDrawn.ParsedSuccessfully && (parsedJavaScriptDrawn.Files.Count != 0 ||
			                                                 parsedJavaScriptDrawn.Subdirectories.Count != 0))
			{
				return parsedJavaScriptDrawn;
			}

			IHtmlCollection<IElement> listItems = htmlDocument.QuerySelectorAll("ul#root li");

			if (listItems.Length != 0)
			{
				WebDirectory result = ParseListItemsDirectoryListing(baseUrl, parsedWebDirectory, listItems, checkParents);

				if (result.ParsedSuccessfully || result.Error)
				{
					return result;
				}
			}

			IHtmlCollection<IElement> tables = htmlDocument.QuerySelectorAll("table");

			if (tables.Length != 0)
			{
				WebDirectory result = ParseDirLIST(baseUrl, parsedWebDirectory, htmlDocument, tables);

				if (result.ParsedSuccessfully)
				{
					return result;
				}

				result = ParseTablesDirectoryListing(baseUrl, parsedWebDirectory, tables, checkParents);

				if (result.Files.Count != 0 || result.Subdirectories.Count != 0 || result.Error)
				{
					return result;
				}
			}

			IHtmlCollection<IElement> materialDesignListItems = htmlDocument.QuerySelectorAll("ul.mdui-list li");

			if (materialDesignListItems.Length != 0)
			{
				return ParseMaterialDesignListItemsDirectoryListing(baseUrl, parsedWebDirectory, materialDesignListItems, checkParents);
			}

			if (htmlDocument.QuerySelectorAll("#content ul#file-list li").Length == 2)
			{
				return ParseDirectoryLister01DirectoryListing(baseUrl, parsedWebDirectory, htmlDocument, checkParents);
			}

			if (htmlDocument.QuerySelectorAll("body > div[x-data=\"application\"]").Length == 1)
			{
				return ParseDirectoryLister02DirectoryListing(baseUrl, parsedWebDirectory, htmlDocument, checkParents);
			}

			listItems = htmlDocument.QuerySelectorAll(".list-group li");

			if (listItems.Length != 0)
			{
				WebDirectory result = ParseListItemsDirectoryListing(baseUrl, parsedWebDirectory, listItems, checkParents);

				if (result.ParsedSuccessfully || result.Error)
				{
					return result;
				}
			}

			listItems = htmlDocument.QuerySelectorAll("ul li");

			if (listItems.Length != 0)
			{
				WebDirectory result = ParseListItemsDirectoryListing(baseUrl, parsedWebDirectory, listItems, checkParents);

				if (result.ParsedSuccessfully || result.Error)
				{
					return result;
				}
			}

			// Latest fallback
			IHtmlCollection<IElement> links = htmlDocument.QuerySelectorAll("a");

			if (links.Length != 0)
			{
				parsedWebDirectory = ParseLinksDirectoryListing(baseUrl, parsedWebDirectory, links, checkParents);
			}

			parsedWebDirectory = await ParseDirectoryListingModel01(baseUrl, parsedWebDirectory, htmlDocument, httpClient);

			CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

			if (parsedWebDirectory.Subdirectories.Count == 0 && parsedWebDirectory.Files.Count == 0 &&
			    !OpenDirectoryIndexer.Session?.ProcessedBrowserUrls.Contains(webDirectory.Url) == false &&
			    htmlDocument.QuerySelector("noscript") != null && htmlDocument.QuerySelector("script") != null)
			{
				Program.Logger.Warning("No directories and files found on {url}, but did find a <noscript> tag, maybe a JavaScript challenge in there which is unsupported", webDirectory.Url);

				if (!OpenDirectoryIndexer.Session.CommandLineOptions.NoBrowser && httpClient is not null)
				{
					OpenDirectoryIndexer.Session.ProcessedBrowserUrls.Add(webDirectory.Url);

					await SemaphoreSlimBrowser.WaitAsync();

					try
					{
						Program.Logger.Warning("Trying to retrieve HTML through browser for {url}", webDirectory.Url);

						if (OpenDirectoryIndexer.Session.MaxThreads != 1)
						{
							Program.Logger.Warning("Reduce threads to {threads} because of possible Browser JavaScript", 1);
							OpenDirectoryIndexer.Session.MaxThreads = 1;
						}

						Program.Logger.Warning("Starting Browser..");
						using BrowserContext browserContext = new(socketsHttpHandler.CookieContainer);
						await browserContext.InitializeAsync();
						Program.Logger.Warning("Started Browser");

						Program.Logger.Warning("Retrieving HTML through Browser..");
						string browserHtml = await browserContext.GetHtml(webDirectory.Url);
						Program.Logger.Warning("Retrieved HTML through Browser");

						// Transfer cookies to HttpClient, so hopefully the following requests can be done with the help of cookies
						CookieParam[] cookieParams = await browserContext.GetCookiesAsync();

						BrowserContext.AddCookiesToContainer(socketsHttpHandler.CookieContainer, cookieParams);

						if (OpenDirectoryIndexer.Session.MaxThreads != OpenDirectoryIndexer.Session.CommandLineOptions.Threads)
						{
							Program.Logger.Warning("Increasing threads back to {threads}", OpenDirectoryIndexer.Session.CommandLineOptions.Threads);
							OpenDirectoryIndexer.Session.MaxThreads = OpenDirectoryIndexer.Session.CommandLineOptions.Threads;
						}

						if (browserHtml != html)
						{
							return await ParseHtml(webDirectory, browserHtml, httpClient, socketsHttpHandler);
						}
					}
					finally
					{
						SemaphoreSlimBrowser.Release();
					}
				}
			}

			if (parsedWebDirectory.Subdirectories.Count == 0 && parsedWebDirectory.Files.Count == 0 &&
			    OpenDirectoryIndexer.Session?.ProcessedBrowserUrls.Any() == false &&
			    htmlDocument.QuerySelector("iframe") != null)
			{
				IEnumerable<Uri> iframeUrls = htmlDocument.QuerySelectorAll<IHtmlInlineFrameElement>("iframe").Select(x => new Uri(OpenDirectoryIndexer.Session.Root.Uri, x.GetAttribute("src")));

				Program.Logger.Warning("No directories and files found on {url}, but did find <iframe> tag(s), you could try this url(s):\n{iframeUrls}", webDirectory.Url, string.Join(Environment.NewLine, iframeUrls));
			}

			return parsedWebDirectory;
		}
		catch (FriendlyException ex)
		{
			Program.Logger.Error(ex.Message, "Exception when parsing {url}", parsedWebDirectory.Url);

			parsedWebDirectory.Error = true;
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Exception when parsing {url}", parsedWebDirectory.Url);

			parsedWebDirectory.Error = true;
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static async Task<WebDirectory> ParseCopypartyListingAsync(string baseUrl, HttpClient httpClient, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, string html)
	{
		return await Copyparty.ParseIndex(baseUrl, httpClient, parsedWebDirectory, htmlDocument, html);
	}

	private static WebDirectory ParseDirLIST(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> tables)
	{
		if (htmlDocument.Title.StartsWith("dirLIST - Index of:"))
		{
			List<IElement> dirListTables = tables.Where(t => t.GetAttribute("width") == "725").ToList();

			if (dirListTables.Any(t => t.TextContent.Contains("No files or folders in this directory")))
			{
				parsedWebDirectory.Parser = "ParseDirLIST";
				parsedWebDirectory.ParsedSuccessfully = true;
				return parsedWebDirectory;
			}

			if (dirListTables.Count >= 3)
			{
				parsedWebDirectory.ParsedSuccessfully = true;

				IHtmlCollection<IElement> entries = dirListTables[3].QuerySelectorAll("td.folder_bg, td.file_bg1, td.file_bg2");

				foreach (IElement entry in entries)
				{
					IHtmlAnchorElement link = entry.QuerySelector("a") as IHtmlAnchorElement;

					bool isDirectory = entry.Matches("td.folder_bg");

					if (link is not null)
					{
						Library.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

						if (isDirectory)
						{
							UrlEncodingParser urlEncodingParser = new(fullUrl);

							string directoryName;

							if (Library.IsBase64String(urlEncodingParser["folder"]))
							{
								directoryName = Encoding.UTF8.GetString(Convert.FromBase64String(urlEncodingParser["folder"]));
							}
							else
							{
								directoryName = link.TextContent.Trim();
							}

							parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
							{
								Parser = "ParseDirLIST",
								Url = fullUrl,
								Name = directoryName
							});
						}
						else
						{
							parsedWebDirectory.Files.Add(new WebFile
							{
								Url = fullUrl,
								FileName = Path.GetFileName(WebUtility.UrlDecode(fullUrl)),
								FileSize = null
							});
						}
					}
				}
			}
		}

		return parsedWebDirectory;
	}

	private static WebDirectory ParseJavaScriptDrawn(string baseUrl, WebDirectory parsedWebDirectory, string html)
	{
		Regex regexDirectory = new("_d\\('(?<DirectoryName>.*)','(?<Date>.*)','(?<Link>.*)'\\)");
		Regex regexFile = new("_f\\('(?<FileName>.*)',(?<FileSize>\\d*),'(?<Date>.*)','(?<Link>.*)',(?<UnixTimestamp>\\d*)\\)");

		MatchCollection matchCollectionDirectories = regexDirectory.Matches(html);
		MatchCollection matchCollectionFiles = regexFile.Matches(html);

		if (matchCollectionDirectories.Count != 0 || matchCollectionFiles.Count != 0)
		{
			parsedWebDirectory.ParsedSuccessfully = true;

			foreach (Match directory in matchCollectionDirectories.Cast<Match>())
			{
				// Remove possible file part (index.php) from url
				if (!string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(baseUrl))))
				{
					UrlEncodingParser urlEncodingParser = new(baseUrl);
					urlEncodingParser.AllKeys.ToList().ForEach(key => urlEncodingParser.Remove(key));
					baseUrl = urlEncodingParser.ToString();

					baseUrl = new Uri(baseUrl.Replace(Path.GetFileName(new Uri(baseUrl).AbsolutePath), string.Empty)).ToString();
				}

				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseJavaScriptDrawn",
					Url = baseUrl + directory.Groups["Link"].Value,
					Name = Uri.UnescapeDataString(directory.Groups["DirectoryName"].Value)
				});
			}

			foreach (Match file in matchCollectionFiles.Cast<Match>())
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = baseUrl + Path.GetFileName(WebUtility.UrlDecode(Uri.UnescapeDataString(file.Groups["Link"].Value))),
					FileName = Path.GetFileName(WebUtility.UrlDecode(Uri.UnescapeDataString(file.Groups["FileName"].Value))),
					FileSize = FileSizeHelper.ParseFileSize(file.Groups["FileSize"].Value)
				});
			}
		}

		return parsedWebDirectory;
	}

	[GeneratedRegex(@"\$\.get\('(?<DirectoryIndexFile>.*)',")]
	private static partial Regex RegexParseDirectoryListingModel01();

	private static async Task<WebDirectory> ParseDirectoryListingModel01(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, HttpClient httpClient)
	{
		// If anyone knows which directory listing this is... :P
		IElement fileManager = htmlDocument.QuerySelector("div.filemanager");

		if (fileManager != null)
		{
			IElement script = htmlDocument.QuerySelector("script[src*=\"script.js\"]");

			if (script != null)
			{
				Uri scriptUri = new(new Uri(baseUrl), script.Attributes["src"].Value);
				string scriptBody = await httpClient.GetStringAsync(scriptUri);

				Match regexMatch = RegexParseDirectoryListingModel01().Match(scriptBody);

				if (regexMatch.Success)
				{
					Uri directoryIndexFile = new(new Uri(baseUrl), regexMatch.Groups["DirectoryIndexFile"].Value);

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
					parsedWebDirectory.ParsedSuccessfully = true;
					parsedWebDirectory.Parser = "DirectoryListingModel01";
				}
			}
		}

		return parsedWebDirectory;
	}

	private static WebDirectory ConvertDirectoryListingModel01(string baseUrl, WebDirectory parsedWebDirectory, DirectoryListingModel01 directoryListingModel)
	{
		Uri directoryUri = new(new Uri(baseUrl), directoryListingModel.Path);
		string directoryFullUrl = directoryUri.ToString();

		WebDirectory webDirectory = new(parsedWebDirectory.ParentDirectory)
		{
			Url = directoryFullUrl,
			Name = directoryListingModel.Name
		};

		foreach (DirectoryListingModel01 item in directoryListingModel.Items)
		{
			Uri uri = new(new Uri(baseUrl), item.Path);
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

	private static WebDirectory ParseCustomDivListing(ref string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> divElements, bool checkParents)
	{
		foreach (IElement divElement in divElements)
		{
			string size = divElement.QuerySelector("em")?.TextContent.Trim();
			IElement link = divElement.QuerySelector("a");

			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			bool isFile = IsFileSize(size);

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseCustomDivListing",
					Url = fullUrl,
					Name = link.QuerySelector("strong").TextContent.TrimEnd('/')
				});
			}
			else
			{
				UrlEncodingParser urlEncodingParser = new(fullUrl);

				string fileName = new Uri(fullUrl).AbsolutePath;

				if (urlEncodingParser["download"] != null)
				{
					fileName = urlEncodingParser["download"];
				}

				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fileName)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseCustomDivListing2(ref string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> divElements, bool checkParents)
	{
		foreach (IElement divElement in divElements)
		{
			bool isFile = divElement.ClassList.Contains("afile");

			IElement link = divElement.QuerySelector("a");

			if (link is null)
			{
				continue;
			}

			if (!isFile)
			{
				string linkHref = link.Attributes["data-href"]?.Value;
				link.SetAttribute("href", linkHref);
			}

			if (!IsValidLink(link))
			{
				continue;
			}

			Uri uri = new(new Uri(baseUrl), link.Attributes["href"]?.Value);
			string fullUrl = uri.ToString();

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseCustomDivListing2",
					Url = fullUrl,
					Name = link.TextContent
				});
			}
			else
			{
				string fileName = new Uri(fullUrl).AbsolutePath;
				string size = divElement.QuerySelector(".sz")?.TextContent.Trim();

				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fileName)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseHfsListing(ref string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> divElements, bool checkParents)
	{
		foreach (IElement divElement in divElements)
		{
			IElement link = divElement.QuerySelector("a");

			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out _, out Uri uri, out string fullUrl);

			bool isFile = !divElement.ClassList.Contains("item-type-folder");

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseHfsListing",
					Url = fullUrl,
					Name = WebUtility.UrlDecode(uri.Segments.Last()).Trim().TrimEnd(trimChars)
				});
			}
			else
			{
				string fileName = new Uri(fullUrl).AbsolutePath;
				string size = divElement.QuerySelector(".item-size")?.TextContent.Trim();

				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fileName)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseIpfsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, bool checkParents)
	{
		foreach (IElement tableRow in htmlDocument.QuerySelectorAll("table tr"))
		{
			IElement link = tableRow.QuerySelector("a");

			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			string size = tableRow.QuerySelector("td:nth-child(3)")?.TextContent.Trim();
			bool isFile = IsFileSize(size);

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseIpfsDirectoryListing",
					Url = fullUrl,
					Name = link?.TextContent
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

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseDirectoryListingDoctComDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool checkParents)
	{
		foreach (IElement listItem in listItems)
		{
			IElement link = listItem.QuerySelector("a");

			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			string size = listItem.QuerySelector(".file-size")?.TextContent.Trim();
			bool isFile = IsFileSize(size);

			if (!isFile)
			{
				if (listItem.Attributes["data-name"]?.Value != "..")
				{
					parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
					{
						Parser = "ParseDirectoryListingDoctComDirectoryListing",
						Url = fullUrl,
						Name = listItem.Attributes["data-name"]?.Value
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

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseSnifDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> snifTableRows, bool checkParents)
	{
		IElement table = snifTableRows.First().Parent("table");

		Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);

		KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
		int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

		KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
		int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

		foreach (IElement tableRow in snifTableRows)
		{
			if (tableRow.ClassList.Contains("snHeading") || tableRow.QuerySelector("td").ClassList.Contains("snDir"))
			{
				continue;
			}

			IHtmlAnchorElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a") as IHtmlAnchorElement;

			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();
			bool isFile = IsFileSize(size) && !size.Contains("item");

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseSnifDirectoryListing",
					Url = fullUrl,
					Name = link?.Title
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = link?.Title,
					FileSize = long.Parse(string.Join(null, Regex.Split((tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex}) span") as IHtmlSpanElement).Title, "[^\\d]")))
				});
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParsePureDirectoryListing(ref string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> pureTableRows, bool checkParents)
	{
		string urlFromBreadcrumbs = Uri.EscapeDataString(string.Join("/", htmlDocument.QuerySelectorAll(".breadcrumbs_main .breadcrumb").Where(b => !b.ClassList.Contains("smaller")).Select(b => b.TextContent)) + "/");

		// Remove possible file part (index.html) from url
		if (!string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(baseUrl))))
		{
			baseUrl = baseUrl.Replace(Path.GetFileName(WebUtility.UrlDecode(baseUrl)), string.Empty);
		}

		// /. is for problem displaying a folder starting with a dot
		string urlFromBaseUrl = baseUrl.Remove(0, new Uri(baseUrl).Scheme.Length + new Uri(baseUrl).Host.Length + 3).Replace("/.", "/");
		urlFromBaseUrl = urlFromBaseUrl.Replace("%23", "#");

		if (urlFromBreadcrumbs == urlFromBaseUrl || urlFromBreadcrumbs == Uri.EscapeDataString(urlFromBaseUrl))
		{
			IElement table = pureTableRows.First().Parent("table");

			Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);

			KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
			int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

			KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
			int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

			foreach (IElement tableRow in pureTableRows)
			{
				IElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a");

				if (!IsValidLink(link))
				{
					continue;
				}

				string linkHref = link.TextContent;
				Uri uri = new(new Uri(baseUrl), Uri.EscapeDataString(linkHref));
				string fullUrl = uri.ToString();
				string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();
				bool isFile = !tableRow.ClassList.Contains("dir");

				if (!isFile)
				{
					if (fullUrl.Contains('#'))
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
		else
		{
			parsedWebDirectory.Error = true;
			Program.Logger.Error("Directory listing returns different directory than requested! Expected: {urlFromBaseUrl}, Actual: {urlFromBreadcrumbs}", urlFromBaseUrl, urlFromBreadcrumbs);
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseH5aiDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> h5aiTableRows, bool checkParents)
	{
		IElement table = h5aiTableRows.First().Parent("table");

		Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);

		KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
		int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

		KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
		int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

		foreach (IElement tableRow in h5aiTableRows)
		{
			IHtmlAnchorElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a") as IHtmlAnchorElement;

			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();
			bool isFile = !string.IsNullOrWhiteSpace(size);
			IElement image = tableRow.QuerySelector("img");

			if (isFile && image != null && image.HasAttribute("alt") && image.Attributes["alt"]?.Value == "folder")
			{
				isFile = false;
			}

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseH5aiDirectoryListing",
					Url = fullUrl,
					Name = link?.TextContent.Trim()
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = link?.TextContent.Trim(),
					FileSize = FileSizeHelper.ParseFileSize(tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})").TextContent)
				});
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseTablesDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> tables, bool checkParents)
	{
		// Dirty solution..
		bool hasSeperateDirectoryAndFilesTables = false;

		ConcurrentList<WebDirectory> results = [];

		foreach (IElement table in tables)
		{
			WebDirectory webDirectoryCopy = JsonConvert.DeserializeObject<WebDirectory>(JsonConvert.SerializeObject(parsedWebDirectory));
			webDirectoryCopy.ParentDirectory = parsedWebDirectory.ParentDirectory;

			Dictionary<int, HeaderInfo> tableHeaders = GetTableHeaders(table);
			webDirectoryCopy.HeaderCount = tableHeaders.Count(th => th.Value.Type != HeaderType.Unknown);

			KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
			int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;

			KeyValuePair<int, HeaderInfo> nameHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileName);
			int nameHeaderColumnIndex = nameHeader.Value != null ? nameHeader.Key : 0;

			KeyValuePair<int, HeaderInfo> descriptionHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.Description);
			int descriptionHeaderColumnIndex = descriptionHeader.Value != null ? descriptionHeader.Key : 0;

			// Extra fallback
			if (fileSizeHeaderColumnIndex != 0 && nameHeaderColumnIndex == 0)
			{
				foreach (IElement tableRow in table.QuerySelectorAll("tbody tr"))
				{
					if (nameHeaderColumnIndex != 0 || tableRow.QuerySelectorAll("a").Length != 1)
					{
						continue;
					}

					foreach (IElement tableColumn in tableRow.QuerySelectorAll("td"))
					{
						if (tableColumn.QuerySelector("a") == null)
						{
							continue;
						}

						nameHeaderColumnIndex = tableColumn.Index();
						break;
					}
				}
			}

			if (fileSizeHeaderColumnIndex == 0 && nameHeaderColumnIndex == 0)
			{
				if (table.QuerySelector("a") != null)
				{
					webDirectoryCopy = ParseLinksDirectoryListing(baseUrl, webDirectoryCopy, table.QuerySelectorAll("a"), checkParents);
				}
			}
			else
			{
				webDirectoryCopy.ParsedSuccessfully = true;

				foreach (IElement tableRow in table.QuerySelectorAll("tbody tr"))
				{
					if (tableRow.QuerySelector("img[alt=\"[ICO]\"]") != null ||
					    tableRow.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
					    tableRow.QuerySelector("a") == null ||
					    tableRow.QuerySelector("th") != null ||
					    tableRow.ClassList.Contains("snHeading") ||
					    tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})") == null ||
					    tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})").TextContent.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase) ||
					    tableRow.QuerySelector("table") != null)
					{
						continue;
					}

					bool addedEntry = false;

					foreach (IElement link in tableRow.QuerySelectorAll("a"))
					{
						if (addedEntry || !IsValidLink(link))
						{
							continue;
						}

						addedEntry = true;

						Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

						fullUrl = StripUrl(fullUrl);

						bool hasFolderIcon = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex}) i.bi-folder") != null;
						UrlEncodingParser urlEncodingParser = new(fullUrl);

						IElement imageElement = tableRow.QuerySelector("img");
						bool isDirectory = hasFolderIcon || tableRow.ClassList.Contains("dir") ||
						                   (imageElement != null &&
						                    (
							                    (imageElement.HasAttribute("alt") &&
							                     imageElement.Attributes["alt"].Value == "[DIR]") ||
							                    (imageElement.HasAttribute("src") &&
							                     (Path.GetFileName(imageElement.Attributes["src"].Value)
								                      .Contains("dir") ||
							                      Path.GetFileName(imageElement.Attributes["src"].Value)
								                      .Contains("folder"))) ||
							                    urlEncodingParser["dirname"] != null
						                    ));

						string description = tableRow.QuerySelector($"td:nth-child({descriptionHeaderColumnIndex})")?.TextContent.Trim();
						string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim().Replace(" ", string.Empty);

						size ??= tableRow.QuerySelector(".size")?.TextContent.Trim();

						bool isFile = urlEncodingParser["file"] != null ||
							!isDirectory &&
							(urlEncodingParser["dir"] == null && (
								(fileSizeHeader.Value == null && !linkHref.EndsWith("/")) ||
								(IsFileSize(size) && size != "0.00b" && !string.IsNullOrWhiteSpace(size) &&
								 (size?.Contains("item")).Value != true && !linkHref.EndsWith("/"))
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

							if (urlEncodingParser["dirname"] != null || hasFolderIcon)
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

							if (link.ClassList.Contains("name"))
							{
								directoryName = link.TextContent.Trim();

								if (directoryName.StartsWith(".."))
								{
									continue;
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

							if (urlEncodingParser["url"] != null)
							{
								filename = Path.GetFileName(WebUtility.UrlDecode(new Uri(urlEncodingParser["url"]).AbsolutePath));
							}

							if (urlEncodingParser["file"] != null)
							{
								filename = Path.GetFileName(WebUtility.UrlDecode(urlEncodingParser["file"]));
							}

							if (string.IsNullOrWhiteSpace(filename))
							{
								filename = link.TextContent;
							}

							if (link.ClassList.Contains("name"))
							{
								filename = link.TextContent.Trim();
							}

							if (urlEncodingParser.Count == 0 && filename.Equals("index.php", StringComparison.InvariantCultureIgnoreCase))
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

			results.Add(webDirectoryCopy);
		}

		if (!hasSeperateDirectoryAndFilesTables)
		{
			parsedWebDirectory = results.Where(r => (r.ParsedSuccessfully || r.Error) && (r.Files.Count > 0 || r.Subdirectories.Count > 0))
				                     .OrderByDescending(r => r.HeaderCount)
				                     .ThenByDescending(r => r.TotalDirectoriesIncludingUnfinished + r.TotalFiles)
				                     .FirstOrDefault() ?? parsedWebDirectory;
		}
		else
		{
			parsedWebDirectory.Subdirectories = [.. results.SelectMany(r => r.Subdirectories)];
			parsedWebDirectory.Files = [.. results.SelectMany(r => r.Files)];
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	[GeneratedRegex(@"(?:<img.*>\s*)+<a.*?>.*?<\/a>\S*\s*(?<Modified>\d*-(?:[a-zA-Z]*|\d*)-\d*\s*\d*:\d*(:\d*)?)?\s*(?<FileSize>\S+)?(\s*(?<Description>.*))?")]
	private static partial Regex RegexRegexParser1();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser1 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser1().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") != null ||
			parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
			parsedLine.QuerySelector("a") == null ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = IsFileSize(match.Groups["FileSize"].Value.Trim()) && parsedLine.QuerySelector("img[alt=\"[DIR]\"]") == null;

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
				Program.Logger.Error(ex, "Error parsing with RegexParser1");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"<a.*<\/a>\s*(?<DateTime>\d+-\w+-\d+\s\d+:\d{0,2}|-)\s*(?<FileSize>\S+\s?\S*)?\s*\S*")]
	private static partial Regex RegexRegexParser2();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser2 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser2().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);
		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		string fileSizeGroup = match.Groups["FileSize"].Value.Trim();

		bool isFile = long.TryParse(fileSizeGroup, out long parsedFileSize);
		long? fileSize = parsedFileSize;

		if (!isFile && IsFileSize(fileSizeGroup))
		{
			fileSize = FileSizeHelper.ParseFileSize(fileSizeGroup);
			isFile = fileSize is not null;
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

		return match.Success;
	};

	[GeneratedRegex(@"(?<Modified>\d+[\.-](?:[a-zA-Z]*|\d+)[\.-]\d+(?:\s*\d*:\d*(?::\d*)?)?)(?:<img.*>\s*)?\S*\s*(?<FileSize>\S+)\s*?<[aA].*<\/[aA]>")]
	private static partial Regex RegexRegexParser3();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser3 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser3().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") != null ||
			parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
			parsedLine.QuerySelector("a") == null ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;" && match.Groups["FileSize"].Value.Trim() != "DIR";

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
				Program.Logger.Error(ex, "Error parsing with RegexParser3");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"\s*(?<Modified>[A-z]*,\s*[A-z]*\s*\d*, \d*\s*\d*:\d*\s*[APM]*)\s+(?<FileSize>\S*)\s+<a.*<\/a>")]
	private static partial Regex RegexRegexParser4();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser4 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser4().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") != null ||
			parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
			parsedLine.QuerySelector("a") == null ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

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
				Program.Logger.Error(ex, "Error parsing with RegexParser4");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"\s*(?<Modified>\d*-\d*-\d*\s*[오전후]*\s*\d*:\d*)\s*(?<FileSize>\S*)\s+<[aA].*<\/[aA]>")]
	private static partial Regex RegexRegexParser5();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser5 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser5().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") != null ||
			parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
			parsedLine.QuerySelector("a") == null ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

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
				Program.Logger.Error(ex, "Error parsing with RegexParser5");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"(?<Modified>\d+\/\d+\/\d+(\s*\d+:\d+\s+[APM]+)?)\s+(?<FileSize>\S*)\s*<a.*<\/a>")]
	private static partial Regex RegexRegexParser6();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser6 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser6().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("img[alt=\"[ICO]\"]") != null ||
			parsedLine.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
			parsedLine.QuerySelector("a") == null ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

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
				Program.Logger.Error(ex, "Error parsing with RegexParser6");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"(?i)(?<FileMode>[d-]r[-w][x-])\s*\d*\s*(?<FileSize>-?\d*)\s*(\S{3}\s*\d*\s*(?:\d*:\d*(:\d*)?|\d*\.?))\s*(<a.*<\/a>\/?)", RegexOptions.None, "en-NL")]
	private static partial Regex RegexRegexParser7();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser7 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser7().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("a") == null)
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		string fileMode = match.Groups["FileMode"].Value.ToLowerInvariant();
		bool isFile = !fileMode.StartsWith('d') && !fileMode.StartsWith('l');

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

				if (fileSize.StartsWith('-'))
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
				Program.Logger.Error(ex, "Error parsing with RegexParser7");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"^\s*(?<Link><a.*<\/a>)\s+(?:(?<Day>\d+)(?<Month>\D+)(?<Year>\d+))(?:\s+(?<Hour>\d+):(?<Minute>\d+))(?:\s+)?(?<FileSize>\S+)?")]
	private static partial Regex RegexRegexParser8();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser8 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser8().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("a") == null)
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		string fileSizeString = match.Groups["FileSize"].Value;
		long? fileSize = FileSizeHelper.ParseFileSize(fileSizeString);

		bool isFile = !string.IsNullOrWhiteSpace(fileSizeString) && fileSizeString.Trim() != "-" &&
			            // This is not perfect.. Specific block sizes
			            (fileSize is not 32768 or 65536);

		if (!isFile)
		{
			string directoryName = Path.GetDirectoryName(WebUtility.UrlDecode(uri.Segments.Last()));

			if (string.IsNullOrWhiteSpace(directoryName))
			{
				directoryName = WebUtility.UrlDecode(linkHref);
			}

			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser8",
				Url = fullUrl,
				Name = directoryName
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
					FileSize = fileSize
				});
			}
			catch (Exception ex)
			{
				Program.Logger.Error(ex, "Error parsing with RegexParser8");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"^\s*(?<Link><a.*<\/a>)\s*(?<IsDirectory>\/?)(?<FileSize>\S+)?")]
	private static partial Regex RegexRegexParser9();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser9 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser9().Match(line);

		if (!match.Success ||
			(match.Groups["IsDirectory"].Success &&
			    !string.IsNullOrWhiteSpace(match.Groups["IsDirectory"].Value)) ==
			match.Groups["FileSize"].Success)
		{
			return match.Success;
		}

		if (match.Groups["FileSize"].Value.Contains('<'))
		{
			return false;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("a") == null)
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = !string.IsNullOrWhiteSpace(match.Groups["FileSize"].Value) &&
			            match.Groups["FileSize"].Value.Trim() != "-";

		if (!isFile)
		{
			string directoryName = Path.GetDirectoryName(WebUtility.UrlDecode(uri.Segments.Last()));

			if (string.IsNullOrWhiteSpace(directoryName))
			{
				directoryName = WebUtility.UrlDecode(linkHref);
			}

			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser9",
				Url = fullUrl,
				Name = directoryName
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
				Program.Logger.Error(ex, "Error parsing with RegexParser9");
			}
		}

		return match.Success;
	};

	[GeneratedRegex(@"(?<Dir>[\-ld])(?<Permissions>(?:[\-r][\-w][\-xs]){1,3})\s+(?<Owner>\w+)\s+(?<Group>\w+)\s+(?<Month>\S{3})\s+(?<Day>\d+)\s+(?<Year>\d+)\s+(?<FileSize>\d+\s+?\w+)?\s+(?:[\w&;]+\s+)?(?<Link><a.*<\/a>\/?)?")]
	private static partial Regex RegexRegexParser10();

	private static readonly Func<WebDirectory, string, string, Task<bool>> RegexParser10 = async (webDirectory, baseUrl, line) =>
	{
		Match match = RegexRegexParser10().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		IHtmlDocument parsedLine = await HtmlParser.ParseDocumentAsync(line);

		if (parsedLine.QuerySelector("a") == null)
		{
			return match.Success;
		}

		IElement link = parsedLine.QuerySelector("a");

		if (!IsValidLink(link))
		{
			return match.Success;
		}

		Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

		string fileMode = match.Groups["Dir"].Value.ToLowerInvariant();

		bool isFile = !fileMode.StartsWith('d') && !fileMode.StartsWith('l');

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser10",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()))
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
				Program.Logger.Error(ex, "Error parsing with RegexParser10");
			}
		}

		return match.Success;
	};

	private static async Task<WebDirectory> ParsePreDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> pres, bool checkParents)
	{
		List<Func<WebDirectory, string, string, Task<bool>>> regexFuncs =
		[
			RegexParser1,
			RegexParser2,
			RegexParser3,
			RegexParser4,
			RegexParser5,
			RegexParser6,
			RegexParser7,
			RegexParser8,
			RegexParser9,
			RegexParser10,
		];

		foreach (IElement pre in pres)
		{
			List<string> lines = Regex.Split(pre.InnerHtml, "\r\n|\r|\n|<br\\S*>|<hr>").ToList();

			foreach (string line in lines)
			{
				foreach (Func<WebDirectory, string, string, Task<bool>> regexFunc in regexFuncs)
				{
					bool succeeded = await regexFunc(parsedWebDirectory, baseUrl, line);

					if (!succeeded)
					{
						continue;
					}

					parsedWebDirectory.Parser = "ParsePreDirectoryListing";
					break;
				}
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseMaterialDesignListItemsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool checkParents)
	{
		int nameIndex = -1;
		int sizeIndex = -1;
		int modifiedIndex = -1;

		IElement headerListItem = listItems.First(li => li.ClassList.Contains("th"));

		int columnIndex = 0;

		foreach (IElement div in headerListItem.QuerySelectorAll("div"))
		{
			IElement italicElement = div.QuerySelector("i");

			IAttr dataSortAttribure = italicElement?.Attributes["data-sort"];

			if (dataSortAttribure != null)
			{
				switch (dataSortAttribure.Value)
				{
					case "name":
						nameIndex = columnIndex;
						break;
					case "date":
						modifiedIndex = columnIndex;
						break;
					case "size":
						sizeIndex = columnIndex;
						break;
				}
			}

			IElement linkElement = div.QuerySelector("a");

			if (linkElement != null)
			{
				UrlEncodingParser urlEncodingParser = new(linkElement.Attributes["href"]?.Value);

				string sortBy = urlEncodingParser["sortby"];

				switch (sortBy)
				{
					case "name":
						nameIndex = columnIndex;
						break;
					case "lastModtime":
						modifiedIndex = columnIndex;
						break;
					case "size":
						sizeIndex = columnIndex;
						break;
				}
			}

			if (div.Children.Length == 0)
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

			if (link?.Attributes["href"] == null)
			{
				continue;
			}

			if (!IsValidLink(link))
			{
				continue;
			}

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

			Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			bool isFile = listItem.ClassList.Contains("file");

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

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseDirectoryLister01DirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, bool checkParents)
	{
		parsedWebDirectory.Parser = "ParseDirectoryLister01DirectoryListing";
		List<HeaderInfo> tableHeaderInfos = [];

		IHtmlCollection<IElement> headerDivs = htmlDocument.QuerySelectorAll("#content > div > div > div");

		tableHeaderInfos.AddRange(headerDivs.Select(GetHeaderInfo));

		IHtmlCollection<IElement> links = htmlDocument.QuerySelectorAll("#content ul#file-list li").Last().QuerySelectorAll("a");

		foreach (IElement link in links)
		{
			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			bool isFile = !link.QuerySelector("i").ClassList.Contains("fa-folder");
			UrlEncodingParser urlEncodingParser = new(fullUrl);

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseDirectoryLister01DirectoryListing",
					// Will fix URLs which ends in spaces etc
					Url = uri.AbsoluteUri,
					Name = WebUtility.UrlDecode(urlEncodingParser["dir"]?.Split("/").Last()),
				});
			}
			else
			{
				try
				{
					List<IElement> divs = link.QuerySelectorAll("div > div").ToList();
					// Remove file info 'column'
					divs.RemoveAt(tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileName) + 1);
					string fileSize = link.QuerySelectorAll("div > div").Skip(2).ToList()[tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileSize)].TextContent;

					parsedWebDirectory.Files.Add(new WebFile
					{
						Url = fullUrl,
						FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
						FileSize = FileSizeHelper.ParseFileSize(fileSize),
					});
				}
				catch (Exception ex)
				{
					Program.Logger.Error(ex, "Error parsing with ParseDirectoryListerDirectoryListing");
				}
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseDirectoryLister02DirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, bool checkParents)
	{
		parsedWebDirectory.Parser = "ParseDirectoryLister02DirectoryListing";
		List<HeaderInfo> tableHeaderInfos = [];

		IHtmlCollection<IElement> headerDivs = htmlDocument.QuerySelectorAll("body > div[x-data=\"application\"] > div > div > div > div > div");

		tableHeaderInfos.AddRange(headerDivs.Select(GetHeaderInfo));

		IHtmlCollection<IElement> links = htmlDocument.QuerySelectorAll("body > div[x-data=\"application\"] > div > div > div > ul > li").Last().QuerySelectorAll("a");

		foreach (IElement link in links)
		{
			if (!IsValidLink(link))
			{
				continue;
			}

			Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			bool isFile = !link.QuerySelector("i").ClassList.Contains("fa-folder");
			UrlEncodingParser urlEncodingParser = new(fullUrl);

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseDirectoryLister02DirectoryListing",
					// Will fix URLs which ends in spaces etc
					Url = uri.AbsoluteUri,
					Name = WebUtility.UrlDecode(urlEncodingParser["dir"]?.Split("/").Last()),
				});
			}
			else
			{
				try
				{
					List<IElement> divs = link.QuerySelectorAll("div > div").ToList();
					// Remove file info 'column'
					divs.RemoveAt(tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileName) + 1);
					string fileSize = link.QuerySelectorAll("div > div").Skip(2).ToList()[tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileSize)].TextContent;

					parsedWebDirectory.Files.Add(new WebFile
					{
						Url = fullUrl,
						FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
						FileSize = FileSizeHelper.ParseFileSize(fileSize),
					});
				}
				catch (Exception ex)
				{
					Program.Logger.Error(ex, "Error parsing with ParseDirectoryListerDirectoryListing");
				}
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	[GeneratedRegex(@"Size: (?<Size>\d+(\.\d+)? \S+)")]
	private static partial Regex RegexParseListItemsDirectoryListingSize();

	private static WebDirectory ParseListItemsDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool checkParents)
	{
		bool firstLink = true;

		Regex regex = RegexParseListItemsDirectoryListingSize();

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

			if (link == null)
			{
				continue;
			}

			Match regexMatch = regex.Match(listItem.TextContent);

			if (regexMatch.Success)
			{
				ProcessLink(baseUrl, parsedWebDirectory, link, "ParseListItemsDirectoryListing", regexMatch.Groups["Size"].Value);
			}
			else
			{
				ProcessLink(baseUrl, parsedWebDirectory, link, "ParseListItemsDirectoryListing");
			}
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static WebDirectory ParseLinksDirectoryListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> links, bool checkParents)
	{
		foreach (IElement link in links)
		{
			ProcessLink(baseUrl, parsedWebDirectory, link, "ParseLinksDirectoryListing");
		}

		CheckParsedResults(parsedWebDirectory, baseUrl, checkParents);

		return parsedWebDirectory;
	}

	private static void ProcessLink(string baseUrl, WebDirectory parsedWebDirectory, IElement link, string parser, string sizeHint = null)
	{
		if (!link.HasAttribute("href"))
		{
			return;
		}

		if (!IsValidLink(link))
		{
			return;
		}

		try
		{
			Library.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			fullUrl = StripUrl(fullUrl);

			UrlEncodingParser urlEncodingParser = new(fullUrl);

			if (uri.Segments.Length == 1 && uri.Segments.Last() == "/" && urlEncodingParser["dir"] == null &&
			    urlEncodingParser["path"] == null)
			{
				return;
			}

			if (baseUrl == StripUrl(fullUrl))
			{
				return;
			}

			parsedWebDirectory.ParsedSuccessfully = true;

			bool directoryListAsp = Path.GetFileName(fullUrl) == "DirectoryList.asp" || fullUrl.Contains("DirectoryList.asp");
			bool dirParam = urlEncodingParser["dir"] != null || urlEncodingParser["path"] != null;

			if (!string.IsNullOrWhiteSpace(Path.GetExtension(fullUrl)) && !directoryListAsp && !dirParam)
			{
				parsedWebDirectory.Parser = parser;

				long? fileSize = null;

				if (link.ParentElement?.NodeName != "BODY")
				{
					fileSize = FileSizeHelper.ParseFileSize(link.ParentElement?.QuerySelector(".fileSize")?.TextContent);
				}

				if (sizeHint != null)
				{
					fileSize = FileSizeHelper.ParseFileSize(sizeHint);
				}

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
						Name = urlEncodingParser["dir"] != null
							? WebUtility.UrlDecode(new Uri(parsedWebDirectory.Uri, urlEncodingParser["dir"]).Segments.Last()).TrimEnd(trimChars)
							: WebUtility.UrlDecode(new Uri(parsedWebDirectory.Uri, urlEncodingParser["path"]).Segments.Last()).TrimEnd(trimChars)
					});
				}
				else if (!directoryListAsp)
				{
					parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
					{
						Parser = parser,
						Url = fullUrl,
						Name = WebUtility.UrlDecode(uri.Segments.Last()).Trim().TrimEnd(trimChars)
					});
				}
				else
				{
					parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
					{
						Parser = parser,
						Url = fullUrl,
						Name = link.TextContent.Trim().TrimEnd(trimChars)
					});
				}
			}
		}
		catch (Exception)
		{
			// Ignore link
		}
	}

	public static void CheckParsedResults(WebDirectory webDirectory, string baseUrl, bool checkParents)
	{
		if (webDirectory.Subdirectories.Count == 0 && webDirectory.Files.Count == 0)
		{
			return;
		}

		foreach (WebDirectory webDirectorySub in webDirectory.Subdirectories)
		{
			webDirectorySub.Url = StripUrl(webDirectorySub.Url);
		}

		if (webDirectory.Uri.Scheme != Constants.UriScheme.Ftp && webDirectory.Uri.Scheme != Constants.UriScheme.Ftps)
		{
			CleanFragments(webDirectory);
		}

		if (checkParents)
		{
			CheckParents(webDirectory, baseUrl);
		}

		CleanDynamicEntries(webDirectory);

		CheckSymlinks(webDirectory);
	}

	/// <summary>
	/// Remove common linux folders
	/// </summary>
	/// <param name="webDirectory">Directory to clean</param>
	private static void CleanDynamicEntries(WebDirectory webDirectory)
	{
		webDirectory.Files.Where(f => f.FileName == "core").ToList().ForEach(f => webDirectory.Files.Remove(f));

		if (webDirectory.Name == "dev")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name is "bus" or "cpu" or "disk"))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}

		if (webDirectory.Name == "lib")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name is "firmware" or "modules"))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}

		if (webDirectory.Name == "proc")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name.All(char.IsDigit)))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}

		if (webDirectory.Name == "run")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name is "sudo" or "user"))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}

		if (webDirectory.Name == "snap")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name == "bin"))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}

		if (webDirectory.Name == "sys")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name is "dev" or "kernel"))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}

		if (webDirectory.Name == "usr")
		{
			webDirectory.Subdirectories
				.Where(d => new List<string> { "bin", "include", "lib", "lib32", "share", "src" }.Contains(d.Name))
				.ToList().ForEach(wd => webDirectory.Subdirectories.Remove(wd));
		}

		if (webDirectory.Name == "var")
		{
			if (webDirectory.Subdirectories.Any(subdir => subdir.Name is "lib" or "run"))
			{
				webDirectory.Subdirectories.Clear();
				webDirectory.Files.Clear();
			}
		}
	}

	private static void CheckParents(WebDirectory webDirectory, string baseUrl)
	{
		Uri baseUri = new(baseUrl);

		List<string> goodSchemes =
		[
			Constants.UriScheme.Https,
			Constants.UriScheme.Http,
			Constants.UriScheme.Ftp,
			Constants.UriScheme.Ftps
		];

		List<string> skipHosts =
		[
			Constants.GoogleDriveDomain,
			Constants.BlitzfilesTechDomain
		];

		webDirectory.Subdirectories.Where(d =>
		{
			Uri uri = new(d.Url);

			return !goodSchemes.Contains(uri.Scheme) || uri.Host != baseUri.Host || skipHosts.Contains(uri.Host) || !SameHostAndDirectoryDirectory(baseUri, uri);
		}).ToList().ForEach(wd => webDirectory.Subdirectories.Remove(wd));

		webDirectory.Files.Where(f =>
		{
			Uri uri = new(f.Url);

			return !goodSchemes.Contains(uri.Scheme) || uri.Host != baseUri.Host || skipHosts.Contains(uri.Host) || !SameHostAndDirectoryFile(uri, baseUri);
		}).ToList().ForEach(f => webDirectory.Files.Remove(f));
	}

	private static void CleanFragments(WebDirectory webDirectory)
	{
		// Directories
		List<WebDirectory> directoriesWithFragments = webDirectory.Subdirectories.Where(wd => wd.Url.Contains('#')).ToList();

		if (directoriesWithFragments.Count != 0)
		{
			foreach (WebDirectory webDir in directoriesWithFragments)
			{
				webDirectory.Subdirectories.Remove(webDir);
			}

			foreach (WebDirectory directoryWithFragments in directoriesWithFragments)
			{
				Uri uri = new(directoryWithFragments.Url);
				directoryWithFragments.Url = uri.GetLeftPart(UriPartial.Query);

				if (webDirectory.Subdirectories.All(wd => wd.Url != directoryWithFragments.Url))
				{
					webDirectory.Subdirectories.Add(directoryWithFragments);
				}
			}
		}

		// Files
		List<WebFile> filesWithFragments = webDirectory.Files.Where(wf => wf.Url.Contains('#')).ToList();

		if (filesWithFragments.Count == 0)
		{
			return;
		}

		webDirectory.Files.Where(wf => wf.Url.Contains('#')).ToList().ForEach(wd => webDirectory.Files.Remove(wd));

		foreach (WebFile fileWithFragment in filesWithFragments)
		{
			Uri uri = new(fileWithFragment.Url);
			fileWithFragment.Url = uri.GetLeftPart(UriPartial.Query);

			if (webDirectory.Files.All(wf => wf.Url != fileWithFragment.Url))
			{
				webDirectory.Files.Add(fileWithFragment);
			}
		}
	}

	private static string StripUrl(string url)
	{
		UrlEncodingParser urlEncodingParser = new(url);

		if (urlEncodingParser.Count != 2)
		{
			return url;
		}

		if (urlEncodingParser.Get("C") != null && urlEncodingParser.Get("O") != null)
		{
			// Remove the default C (column) and O (order) parameters
			urlEncodingParser.Remove("C");
			urlEncodingParser.Remove("O");
		}

		if (urlEncodingParser.Get("sort") != null && urlEncodingParser.Get("order") != null)
		{
			// Remove the default sort and order parameters for https://github.com/TheWaWaR/simple-http-server/
			urlEncodingParser.Remove("sort");
			urlEncodingParser.Remove("order");
		}

		return urlEncodingParser.ToString();
	}

	private static void CheckSymlinks(WebDirectory webDirectory)
	{
		WebDirectory parentWebDirectory = webDirectory.ParentDirectory;

		for (int level = 1; level <= 8; level++)
		{
			if (webDirectory.Uri.Segments.Length <= level || parentWebDirectory == null)
			{
				continue;
			}

			if (webDirectory.Subdirectories.Count != 0 || webDirectory.Files.Count != 0)
			{
				if (CheckDirectoryTheSame(webDirectory, parentWebDirectory))
				{
					Program.Logger.Error("Possible virtual directory or symlink detected (level {level})! SKIPPING! Url: {url}", level, webDirectory.Url);

					webDirectory.Subdirectories = [];
					webDirectory.Files = [];
					webDirectory.Error = true;
					break;
				}
			}

			parentWebDirectory = parentWebDirectory?.ParentDirectory;
		}
	}

	private static bool CheckDirectoryTheSame(WebDirectory webDirectory, WebDirectory parentWebDirectory)
	{
		if (webDirectory.Files.Count != parentWebDirectory.Files.Count ||
		    webDirectory.Subdirectories.Count != parentWebDirectory.Subdirectories.Count)
		{
			return false;
		}

		// TODO: If anyone knows a nice way without JsonConvert, PR!
		if ((parentWebDirectory.Files.Count == 0 ||
		     (JsonConvert.SerializeObject(parentWebDirectory.Files.Select(f => new { f.FileName, f.FileSize })) ==
		      JsonConvert.SerializeObject(webDirectory.Files.Select(f => new { f.FileName, f.FileSize })))) &&
		    (parentWebDirectory.Subdirectories.Count == 0 ||
		     JsonConvert.SerializeObject(parentWebDirectory.Subdirectories.Select(d => d.Name)) ==
		     JsonConvert.SerializeObject(webDirectory.Subdirectories.Select(d => d.Name))))
		{
			return true;
		}

		return false;
	}

	public static string ReplaceCommonDefaultFilenames(string input)
	{
		input = input.Replace("index.shtml", string.Empty);
		input = input.Replace("index.php", string.Empty);
		input = input.Replace("DirectoryList.asp", string.Empty);

		return input;
	}

	public static bool SameHostAndDirectoryFile(Uri baseUri, Uri checkUri)
	{
		string checkUrlWithoutFileName = checkUri.LocalPath;
		checkUrlWithoutFileName = ReplaceCommonDefaultFilenames(checkUrlWithoutFileName);
		string checkUrlFileName = Path.GetFileName(checkUri.ToString());

		if (!string.IsNullOrWhiteSpace(checkUrlFileName))
		{
			checkUrlWithoutFileName = checkUrlWithoutFileName.Replace(checkUrlFileName, string.Empty);
		}

		string baseUrlWithoutFileName = baseUri.LocalPath;
		string baseUrlFileName = Path.GetFileName(baseUri.ToString());

		if (!string.IsNullOrWhiteSpace(baseUrlFileName))
		{
			baseUrlWithoutFileName = baseUri.LocalPath.Replace(baseUrlFileName, string.Empty);
		}

		return baseUri.ToString() == checkUri.ToString() || (baseUri.Host == checkUri.Host && (
			checkUri.LocalPath.StartsWith(baseUri.LocalPath) ||
			checkUri.LocalPath.StartsWith(baseUrlWithoutFileName) ||
			baseUri.LocalPath.StartsWith(checkUrlWithoutFileName)
		));
	}

	public static bool SameHostAndDirectoryDirectory(Uri baseUri, Uri checkUri)
	{
		if (baseUri.ToString() == checkUri.ToString())
		{
			return true;
		}

		if (baseUri.Host != checkUri.Host)
		{
			return false;
		}

		return ReplaceCommonDefaultFilenames(checkUri.LocalPath).StartsWith(ReplaceCommonDefaultFilenames(baseUri.LocalPath));
	}

	/// <summary>
	/// Check simple cases of file size
	/// </summary>
	/// <param name="value">Value to check</param>
	/// <returns>Is it might be a file size</returns>
	private static bool IsFileSize(string value)
	{
		return value != "-" && value != "—" && value != "<Directory>";
		//return !string.IsNullOrWhiteSpace(value) && value != "-" && value != "—" && value != "<Directory>";
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
		Dictionary<int, HeaderInfo> tableHeaders = [];

		IHtmlCollection<IElement> headers = table.QuerySelector("th")?.ParentElement?.QuerySelectorAll("th");

		bool removeFirstRow = false;

		if (headers != null && headers.First().HasAttribute("colspan"))
		{
			headers = null;
		}

		headers ??= table.QuerySelector(".snHeading")?.QuerySelectorAll("td");

		if (headers == null || headers.Length == 0)
		{
			headers = table.QuerySelectorAll("thead td, thead th");
		}

		if (headers.Length == 0)
		{
			headers = table.QuerySelectorAll("tr:nth-child(1) > th");
		}

		if (headers.Length == 0)
		{
			headers = table.QuerySelectorAll("tr:nth-child(1) > td");

			if (headers?.Length > 0)
			{
				removeFirstRow = true;
			}
		}

		if (headers?.Any() != true)
		{
			return tableHeaders;
		}

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

			List<int> fileNameColumnIndex = [];
			List<int> dateColumnIndex = [];
			List<int> fileSizeColumnIndex = [];
			List<int> typeColumnIndex = [];

			int maxColumns = 0;

			foreach (IElement tableRow in table.QuerySelectorAll("tr"))
			{
				List<IElement> rowColumns = tableRow.QuerySelectorAll("td").ToList();

				maxColumns = Math.Max(maxColumns, rowColumns.Count);

				foreach (IElement tableColumn in rowColumns)
				{
					if (tableColumn.QuerySelector("a") != null)
					{
						fileNameColumnIndex.Add(tableColumn.Index());
					}

					if (DateTime.TryParse(tableColumn.TextContent, out DateTime parsedDateTime) && parsedDateTime != DateTime.MinValue)
					{
						dateColumnIndex.Add(tableColumn.Index());
					}

					if (FileSizeHelper.ParseFileSize(tableColumn.TextContent, onlyChecking: true) is not null)
					{
						fileSizeColumnIndex.Add(tableColumn.Index());
					}

					if (tableColumn.QuerySelector("img") != null)
					{
						typeColumnIndex.Add(tableColumn.Index());
					}
				}
			}

			if (fileNameColumnIndex.Count != 0)
			{
				int columnIndex = ((int)Math.Round(fileNameColumnIndex.Average())) + 1;
				columnIndex = Math.Min(columnIndex, maxColumns);

				if (!tableHeaders.ContainsKey(columnIndex))
				{
					tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.FileName });
				}
			}

			if (dateColumnIndex.Count != 0)
			{
				int columnIndex = ((int)Math.Round(dateColumnIndex.Average())) + 1;
				columnIndex = Math.Min(columnIndex, maxColumns);

				if (!tableHeaders.ContainsKey(columnIndex))
				{
					tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.Modified });
				}
			}

			if (fileSizeColumnIndex.Count != 0)
			{
				int columnIndex = ((int)Math.Round(fileSizeColumnIndex.Average())) + 1;
				columnIndex = Math.Min(columnIndex, maxColumns);

				if (!tableHeaders.ContainsKey(columnIndex))
				{
					tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.FileSize });
				}
			}

			if (typeColumnIndex.Count != 0)
			{
				int columnIndex = ((int)Math.Round(typeColumnIndex.Average())) + 1;
				columnIndex = Math.Min(columnIndex, maxColumns);

				if (!tableHeaders.ContainsKey(columnIndex))
				{
					tableHeaders.Add(columnIndex, new HeaderInfo { Type = HeaderType.Type });
				}
			}
		}
		else
		{
			if (tableHeaders.Any(th => th.Value.Type == HeaderType.FileName) &&
			    tableHeaders.Any(th => th.Value.Type == HeaderType.FileSize) && removeFirstRow)
			{
				table.QuerySelector("tr:nth-child(1)")?.Remove();
			}
		}

		IElement firstRow = table.QuerySelector("tbody tr");

		// Correct colspans when using in tbody
		if (firstRow is null)
		{
			return tableHeaders;
		}

		List<IElement> columns = firstRow.QuerySelectorAll("td").ToList();

		for (int i = 0; i < columns.Count; i++)
		{
			IElement tableColumn = columns[i];

			if (!tableColumn.HasAttribute("colspan"))
			{
				continue;
			}

			int colspan = int.Parse(tableColumn.GetAttribute("colspan"));

			foreach (KeyValuePair<int, HeaderInfo> tableHeader in tableHeaders.Where(header => header.Key > i + colspan).ToList())
			{
				HeaderInfo headerInfo = tableHeaders[tableHeader.Key];
				tableHeaders.Remove(tableHeader.Key);

				int newKey = tableHeader.Key - (colspan - 1);

				tableHeaders.TryAdd(newKey, headerInfo);
			}
		}

		return tableHeaders;
	}

	private static HeaderInfo GetHeaderInfo(IElement header)
	{
		string headerName = header.TextContent.Trim();

		HeaderInfo headerInfo = new()
		{
			Header = headerName
		};

		// Someone thought it would be a good idea to include it in a table..
		if (header.QuerySelector("ul") != null)
		{
			return headerInfo;
		}

		headerName = headerName.ToLowerInvariant();

		headerName = Regex.Replace(headerName, @"[^\u00BF-\u1FFF\u2C00-\uD7FF\w]", string.Empty);

		if (headerName == "lastmodified" || headerName == "modified" || headerName.Contains("date") ||
		    headerName.Contains("lastmodification") || headerName.Contains("time") || headerName.Contains("修改时间") ||
		    headerName.Contains("修改日期") || headerName.Contains("最終更新"))
		{
			headerInfo.Type = HeaderType.Modified;
		}

		if (headerName == "type")
		{
			headerInfo.Type = HeaderType.Type;
		}

		if (headerName.Contains("size") || headerName.Contains("file size") || headerName.Contains("filesize") ||
		    headerName.Contains("taille") ||
		    // Creates a problem for a single testcase, need to find a better way
		    //headerName.Contains("größe") ||
		    headerName.Contains("大小") ||
		    headerName.Contains("サイズ")
		   )
		{
			headerInfo.Type = HeaderType.FileSize;
		}

		if (headerName == "description")
		{
			headerInfo.Type = HeaderType.Description;
		}

		// Check this as last one because of generic 'file' in it..
		if (
			headerInfo.Type == HeaderType.Unknown &&
			(
				headerName == "file" ||
				headerName.Contains("name") ||
				headerName.Contains("file name") ||
				headerName.Contains("filename") ||
				headerName == "directory" ||
				headerName.Contains("link") ||
				headerName.Contains("nom") ||
				headerName.Contains("文件") ||
				headerName.Contains("ファイル名")
			)
		)
		{
			headerInfo.Type = HeaderType.FileName;
		}

		return headerInfo;
	}

	private static bool IsValidLink(IElement link)
	{
		if (link == null)
		{
			return false;
		}

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
			linkHref?.StartsWith("javascript:", StringComparison.InvariantCultureIgnoreCase) == false &&
			linkHref?.StartsWith("mailto:", StringComparison.InvariantCultureIgnoreCase) == false &&
			!link.TextContent.Equals("parent directory", StringComparison.InvariantCultureIgnoreCase) &&
			!link.TextContent.Equals("[to parent directory]", StringComparison.InvariantCultureIgnoreCase) &&
			link.TextContent.Trim() != "Name" &&
			linkHref?.Contains("&expand") == false &&
			(!RegexNMSDAD().IsMatch(linkHref) || linkHref.StartsWith("DirectoryList.asp")) &&
			(Path.GetFileName(linkHref) != "DirectoryList.asp" || !string.IsNullOrWhiteSpace(link.TextContent));
	}

	[GeneratedRegex(@"\?[NMSD]=?[AD]")]
	private static partial Regex RegexNMSDAD();
}