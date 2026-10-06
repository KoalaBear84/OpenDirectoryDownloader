using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Listings;
using OpenDirectoryDownloader.Parsing.Listings.Pre;
using OpenDirectoryDownloader.Parsing.Listings.Tables;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Parsing.Sites.AList;
using OpenDirectoryDownloader.Parsing.Sites.BlitzfilesTech;
using OpenDirectoryDownloader.Parsing.Sites.Copyparty;
using OpenDirectoryDownloader.Parsing.Sites.Dropbox;
using OpenDirectoryDownloader.Parsing.Sites.Dufs;
using OpenDirectoryDownloader.Parsing.Sites.FileBrowser;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex.Bhadoo;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex.GdIndex;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex.Go2Index;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex.GoIndex;
using OpenDirectoryDownloader.Parsing.Sites.GitHub;
using OpenDirectoryDownloader.Parsing.Sites.GoFileIO;
using OpenDirectoryDownloader.Parsing.Sites.HFS;
using OpenDirectoryDownloader.Parsing.Sites.Mediafire;
using OpenDirectoryDownloader.Parsing.Sites.PCloud;
using OpenDirectoryDownloader.Parsing.Sites.Pixeldrain;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Urls;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Html;

/// <summary>
/// THE ORDER OF THE CHECKS IS VERY IMPORTANT. Many pages match several of them (a page with a &lt;pre&gt; in a
/// &lt;table&gt; in a &lt;ul&gt;, ...), the first step that recognizes a page wins. This array is the one place that
/// order is defined; the golden parse tests and PipelineOrderTests guard it.
/// </summary>
public static partial class DefaultListingPipeline
{
	private static readonly HtmlParser HtmlParser = new();

	public static ImmutableArray<ListingStep> Steps { get; } =
	[
		// Sites recognized by their host or Server header, before the page is parsed at all
		new("BlitzfilesTech", BlitzfilesTech),
		new("Dropbox", Dropbox),
		new("GitHub", GitHub),
		new("GoFileIO", GoFileIo),
		new("Mediafire", Mediafire),
		new("pCloud (host)", PCloudByHost),
		new("Pixeldrain", Pixeldrain),
		new("HFS (Server header)", HfsByServerHeader),

		new("BuildDom", BuildDom),

		// Sites recognized by their page
		new("IPFS", Ipfs),
		new("Dufs", Dufs),
		new("File Browser", FileBrowser),
		new("AList", AList),
		new("Google Drive index detection", DetectGoogleDriveIndex),
		new("Google Drive index", GoogleDriveIndex),

		new("Remove #sidebar and nav", RemoveSidebarAndNav),

		new("HFS (legacy title)", HfsByTitle),
		new("pCloud (title)", PCloudByTitle),
		new("directory-listing.com", DirectoryListingDotCom),
		new("h5ai", H5ai),
		new("Snif", Snif),
		new("Godir", Godir),

		// Godir reads the breadcrumbs, everything after it must not
		new("Remove .breadcrumb", RemoveBreadcrumbs),

		new("Custom div listing", CustomDivListing),
		new("Custom div listing 2", CustomDivListing2),
		new("HFS (div#files)", HfsDivListing),
		new("copyparty", CopypartyListing),
		new("Raw <pre> shortcut", RawPreShortcut),
		new("<pre>", Pre),
		new("JavaScript drawn", JavaScriptDrawn),
		new("ul#root li", RootListItems),
		new("dirLIST", DirList),
		new("Tables", Tables),
		new("Material design", MaterialDesign),
		new("Directory Lister 01", DirectoryLister01),
		new("Directory Lister 02", DirectoryLister02),
		new(".list-group li", ListGroupItems),
		new("ul li", ListItems),

		// The last resorts, they always complete the result
		new("All links", AllLinks),
		new("Directory listing model 01", DirectoryListingModel01),
		new("Clean up", CleanUp),
		new("Browser", Browser),
		new("Iframe hint", IframeHint)
	];

	private static async ValueTask<StepResult> BlitzfilesTech(ParseContext context)
	{
		return context.Host == Constants.BlitzfilesTechDomain ? StepResult.Done(await BlitzfilesTechParser.ParseIndex(context.Services, context.WebDirectory)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> Dropbox(ParseContext context)
	{
		return context.Host == Constants.DropboxDomain ? StepResult.Done(await DropboxParser.ParseIndex(context.Services, context.WebDirectory, context.Html, context.Response)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> GitHub(ParseContext context)
	{
		return context.Host is Constants.GitHubDomain or Constants.GitHubApiDomain ? StepResult.Done(await GitHubParser.ParseIndex(context.Services, context.WebDirectory)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> GoFileIo(ParseContext context)
	{
		return context.Host == Constants.GoFileIoDomain ? StepResult.Done(await GoFileIOParser.ParseIndex(context.Services, context.WebDirectory)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> Mediafire(ParseContext context)
	{
		return context.Host == Constants.MediafireDomain ? StepResult.Done(await MediafireParser.ParseIndex(context.Services, context.WebDirectory)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> PCloudByHost(ParseContext context)
	{
		return context.Host is Constants.PCloudDomain1 or Constants.PCloudDomain2 && context.Html.Contains("directLinkData") ? StepResult.Done(await PCloudParser.ParseIndex(context.Services, context.WebDirectory, context.Html)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> Pixeldrain(ParseContext context)
	{
		return context.Host == Constants.PixeldrainDomain ? StepResult.Done(await PixeldrainParser.ParseIndex(context.Services, context.WebDirectory, context.Html)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> HfsByServerHeader(ParseContext context)
	{
		string server = context.Response?.Headers.Server?.ToString();

		return server?.StartsWith("hfs", StringComparison.InvariantCultureIgnoreCase) == true ? StepResult.Done(await HfsParser.ParseIndex(context.Services, context.WebDirectory, context.Html, server)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> BuildDom(ParseContext context)
	{
		// A big listing's one <pre> read from the raw text, so its thousands of lines never become DOM nodes
		context.RawPre = context.AllowRawPre && context.Html.Length >= RawPreListing.MinimumLength ? RawPreListing.TryExtract(context.Html) : null;
		context.Document = await HtmlParser.ParseDocumentAsync(context.RawPre?.HtmlWithMarkedPre ?? context.Html);

		return StepResult.Continue;
	}

	private static StepResult Ipfs(ParseContext context)
	{
		return context.WebDirectory.Uri.Host is "ipfs.io" or "gateway.ipfs.io" ? StepResult.Done(IpfsListingParser.Parse(context.BaseUrl, context.Parsed, context.Document, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> Dufs(ParseContext context)
	{
		IHtmlTemplateElement dufsIndexData = context.Document.QuerySelector<IHtmlTemplateElement>("template#index-data");

		if (dufsIndexData is null)
		{
			return StepResult.Continue;
		}

		// The content of <template id="index-data"> is the listing as Base64 encoded JSON
		string dufsIndexDataContent = dufsIndexData.Content.TextContent.Trim();

		if (!UrlHelper.IsBase64String(dufsIndexDataContent))
		{
			return StepResult.Continue;
		}

		dufsIndexDataContent = Encoding.UTF8.GetString(Convert.FromBase64String(dufsIndexDataContent));

		return StepResult.Done(await DufsParser.ParseIndex(context.Services, context.Parsed, dufsIndexDataContent));
	}

	/// <summary>https://github.com/filebrowser/filebrowser</summary>
	private static async ValueTask<StepResult> FileBrowser(ParseContext context)
	{
		if (context.Document.Title != "File Browser" ||
			!context.Document.Scripts.Any(s => s.Source is not null && FileBrowserScriptRegex().IsMatch(s.Source)) ||
			!FileBrowserBaseUrlRegex().IsMatch(context.Html))
		{
			return StepResult.Continue;
		}

		return StepResult.Done(await FileBrowserParser.ParseIndex(context.BaseUrl, context.Services, context.Parsed, context.Document, context.Html));
	}

	private static async ValueTask<StepResult> AList(ParseContext context)
	{
		string generator = context.Document.QuerySelectorAll<IHtmlMetaElement>("meta[name][content]").FirstOrDefault(x => x.Name == "generator")?.Content;

		return generator == "AList V3" ? StepResult.Done(await AListParser.ParseIndex(context.Services, context.Parsed)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> DetectGoogleDriveIndex(ParseContext context)
	{
		GoogleDriveIndexState state = context.Services.Session.SiteState.Get<GoogleDriveIndexState>();

		if (context.Services.Http is null || state.IndexType is not null)
		{
			return StepResult.Continue;
		}

		string googleDriveIndexType = await GoogleDriveIndexDetector.DetectAsync(context);

		if (googleDriveIndexType is not null)
		{
			state.IndexType = googleDriveIndexType;

			if (context.Services.Session.Concurrency.GetCap(ConcurrencyReasons.GoogleDriveIndex) is null)
			{
				context.Logger.LogWarning("Reduce threads to 1 because of Google Drive index");
				context.Services.Session.Concurrency.SetCap(ConcurrencyReasons.GoogleDriveIndex, 1);
			}
		}

		return StepResult.Continue;
	}

	private static async ValueTask<StepResult> GoogleDriveIndex(ParseContext context)
	{
		return context.Services.Session.SiteState.Find<GoogleDriveIndexState>()?.IndexType switch
		{
			GoogleDriveIndexMapping.BhadooIndex => StepResult.Done(await BhadooIndexParser.ParseIndex(context.Document, context.Services, context.WebDirectory)),
			GoogleDriveIndexMapping.GoIndex => StepResult.Done(await GoIndexParser.ParseIndex(context.Services, context.WebDirectory)),
			GoogleDriveIndexMapping.Go2Index => StepResult.Done(await Go2IndexParser.ParseIndex(context.Services, context.WebDirectory)),
			GoogleDriveIndexMapping.GdIndex => StepResult.Done(await GdIndexParser.ParseIndex(context.Services, context.WebDirectory, context.Html)),
			_ => StepResult.Continue
		};
	}

	private static StepResult RemoveSidebarAndNav(ParseContext context)
	{
		RemoveAll(context.Document.QuerySelectorAll("#sidebar"));
		RemoveAll(context.Document.QuerySelectorAll("nav"));

		return StepResult.Continue;
	}

	/// <summary>HFS (legacy) up to 2.3x; 2.4+ is handled by the div#files step</summary>
	private static StepResult HfsByTitle(ParseContext context)
	{
		if (!context.Document.Title.StartsWith("HFS /"))
		{
			return StepResult.Continue;
		}

		IHtmlCollection<IElement> hfsTable = context.Document.QuerySelectorAll("table#files");

		if (hfsTable.Length != 0)
		{
			return StepResult.Done(TableListingParser.Parse(context.BaseUrl, context.Parsed, hfsTable, context.CheckParents, context.Logger));
		}

		return context.Document.QuerySelector("div#files") == null ? StepResult.Done(context.Parsed) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> PCloudByTitle(ParseContext context)
	{
		return context.Document.Title.StartsWith("pCloud - ") && context.Html.Contains("directLinkData") ? StepResult.Done(await PCloudParser.ParseIndex(context.Services, context.WebDirectory, context.Html)) : StepResult.Continue;
	}

	private static StepResult DirectoryListingDotCom(ParseContext context)
	{
		IHtmlCollection<IElement> listItems = context.Document.QuerySelectorAll("#directory-listing li, .directory-listing li");

		return listItems.Length != 0 ? StepResult.Done(DirectoryListingDotComParser.Parse(context.BaseUrl, context.Parsed, listItems, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult H5ai(ParseContext context)
	{
		IHtmlCollection<IElement> rows = context.Document.QuerySelectorAll("#fallback table tr");

		return rows.Length != 0 ? StepResult.Done(H5aiListingParser.Parse(context.BaseUrl, context.Parsed, rows, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult Snif(ParseContext context)
	{
		IHtmlCollection<IElement> rows = context.Document.QuerySelectorAll("table.snif tr");

		return rows.Length != 0 ? StepResult.Done(SnifListingParser.Parse(context.BaseUrl, context.Parsed, rows, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult Godir(ParseContext context)
	{
		IHtmlCollection<IElement> rows = context.Document.QuerySelectorAll("table.listing-table tbody tr");

		return rows.Length != 0 ? StepResult.Done(GodirListingParser.Parse(context.BaseUrl, context.Parsed, context.Document, rows, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult RemoveBreadcrumbs(ParseContext context)
	{
		RemoveAll(context.Document.QuerySelectorAll(".breadcrumb"));

		return StepResult.Continue;
	}

	private static StepResult CustomDivListing(ParseContext context)
	{
		IHtmlCollection<IElement> divElements = context.Document.QuerySelectorAll("div#listing div");

		return divElements.Length != 0 ? StepResult.Done(DivListingParsers.ParseCustomDivListing(context.BaseUrl, context.Parsed, divElements, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult CustomDivListing2(ParseContext context)
	{
		IHtmlCollection<IElement> divElements = context.Document.QuerySelectorAll("div#filelist .tb-row.folder,div#filelist .tb-row.afile");

		return divElements.Length != 0 ? StepResult.Done(DivListingParsers.ParseCustomDivListing2(context.BaseUrl, context.Parsed, divElements, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult HfsDivListing(ParseContext context)
	{
		IHtmlCollection<IElement> divElements = context.Document.QuerySelectorAll("div#files .item");

		return divElements.Length != 0 ? StepResult.Done(DivListingParsers.ParseHfsListing(context.BaseUrl, context.Parsed, divElements, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static async ValueTask<StepResult> CopypartyListing(ParseContext context)
	{
		return context.Document.QuerySelector("#op_bup #u2err") is not null ? StepResult.Done(await Copyparty.ParseIndex(context.BaseUrl, context.Services, context.Parsed, context.Document, context.Html)) : StepResult.Continue;
	}

	private static StepResult RawPreShortcut(ParseContext context)
	{
		if (context.RawPre is null)
		{
			return StepResult.Continue;
		}

		IHtmlCollection<IElement> pres = context.Document.QuerySelectorAll("pre");

		if (pres.Length == 1 && pres[0].HasAttribute(RawPreListing.MarkerAttribute))
		{
			WebDirectory rawResult = PreListingParser.ParseLines(context.BaseUrl, context.Parsed, [context.RawPre.Lines], context.CheckParents, context.Logger);

			if (rawResult.Files.Count != 0 || rawResult.Subdirectories.Count != 0 || rawResult.Error)
			{
				return StepResult.Done(rawResult);
			}
		}

		// Not the shape the shortcut expected, or it found nothing: the later steps need the real DOM
		return StepResult.RestartWithoutRawPre;
	}

	private static StepResult Pre(ParseContext context)
	{
		IHtmlCollection<IElement> pres = context.Document.QuerySelectorAll("pre");

		if (pres.Length == 0)
		{
			return StepResult.Continue;
		}

		WebDirectory result = PreListingParser.ParsePres(context.BaseUrl, context.Parsed, pres, context.CheckParents, context.Logger);

		return result.Files.Count != 0 || result.Subdirectories.Count != 0 || result.Error ? StepResult.Done(result) : StepResult.Continue;
	}

	private static StepResult JavaScriptDrawn(ParseContext context)
	{
		WebDirectory result = JavaScriptDrawnParser.Parse(context.BaseUrl, context.Parsed, context.Html);

		return result.ParsedSuccessfully && (result.Files.Count != 0 || result.Subdirectories.Count != 0) ? StepResult.Done(result) : StepResult.Continue;
	}

	private static StepResult RootListItems(ParseContext context) => ParseListItems(context, "ul#root li");

	private static StepResult DirList(ParseContext context)
	{
		IHtmlCollection<IElement> tables = context.Document.QuerySelectorAll("table");

		if (tables.Length == 0)
		{
			return StepResult.Continue;
		}

		WebDirectory result = DirListParser.Parse(context.BaseUrl, context.Parsed, context.Document, tables);

		return result.ParsedSuccessfully ? StepResult.Done(result) : StepResult.Continue;
	}

	private static StepResult Tables(ParseContext context)
	{
		IHtmlCollection<IElement> tables = context.Document.QuerySelectorAll("table");

		if (tables.Length == 0)
		{
			return StepResult.Continue;
		}

		WebDirectory result = TableListingParser.Parse(context.BaseUrl, context.Parsed, tables, context.CheckParents, context.Logger);

		return result.Files.Count != 0 || result.Subdirectories.Count != 0 || result.Error ? StepResult.Done(result) : StepResult.Continue;
	}

	private static StepResult MaterialDesign(ParseContext context)
	{
		IHtmlCollection<IElement> listItems = context.Document.QuerySelectorAll("ul.mdui-list li");

		return listItems.Length != 0 ? StepResult.Done(MaterialDesignListingParser.Parse(context.BaseUrl, context.Parsed, listItems, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult DirectoryLister01(ParseContext context)
	{
		return context.Document.QuerySelectorAll("#content ul#file-list li").Length == 2 ? StepResult.Done(DirectoryListerParser.Parse01(context.BaseUrl, context.Parsed, context.Document, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult DirectoryLister02(ParseContext context)
	{
		return context.Document.QuerySelectorAll("body > div[x-data=\"application\"]").Length == 1 ? StepResult.Done(DirectoryListerParser.Parse02(context.BaseUrl, context.Parsed, context.Document, context.CheckParents, context.Logger)) : StepResult.Continue;
	}

	private static StepResult ListGroupItems(ParseContext context) => ParseListItems(context, ".list-group li");

	private static StepResult ListItems(ParseContext context) => ParseListItems(context, "ul li");

	private static StepResult ParseListItems(ParseContext context, string selector)
	{
		IHtmlCollection<IElement> listItems = context.Document.QuerySelectorAll(selector);

		if (listItems.Length == 0)
		{
			return StepResult.Continue;
		}

		WebDirectory result = LinkListingParser.ParseListItems(context.BaseUrl, context.Parsed, listItems, context.CheckParents, context.Logger);

		return result.ParsedSuccessfully || result.Error ? StepResult.Done(result) : StepResult.Continue;
	}

	private static StepResult AllLinks(ParseContext context)
	{
		IHtmlCollection<IElement> links = context.Document.QuerySelectorAll("a");

		if (links.Length != 0)
		{
			LinkListingParser.ParseLinks(context.BaseUrl, context.Parsed, links, context.CheckParents, context.Logger);
			context.ResultChecked = true;
		}

		return StepResult.Continue;
	}

	private static async ValueTask<StepResult> DirectoryListingModel01(ParseContext context)
	{
		if (await DirectoryListingModel01Parser.TryParseAsync(context.BaseUrl, context.Parsed, context.Document, context.Services.Http))
		{
			context.ResultChecked = false;
		}

		return StepResult.Continue;
	}

	private static StepResult CleanUp(ParseContext context)
	{
		// Cleaning up twice changes nothing, so only when the links fallback did not already do it
		if (!context.ResultChecked)
		{
			ListingPostProcessor.CheckParsedResults(context.Parsed, context.BaseUrl, context.CheckParents, context.Logger);
		}

		return StepResult.Continue;
	}

	/// <summary>Nothing found, but the page has a &lt;noscript&gt;: it is probably drawn by JavaScript, so get it through a real browser (once per URL).</summary>
	private static async ValueTask<StepResult> Browser(ParseContext context)
	{
		WebDirectory parsed = context.Parsed;
		ParserServices services = context.Services;

		if (parsed.Subdirectories.Count != 0 || parsed.Files.Count != 0 ||
			context.Document.QuerySelector("noscript") == null || context.Document.QuerySelector("script") == null)
		{
			return StepResult.Continue;
		}

		context.Logger.LogWarning("No directories and files found on {url}, but did find a <noscript> tag, maybe a JavaScript challenge in there which is unsupported", context.WebDirectory.Url);

		if (services.Options.NoBrowser || services.Browser is null || services.Http is null || !services.Session.BrowserAttempts.Add(context.WebDirectory.Url))
		{
			return StepResult.Continue;
		}

		context.Logger.LogWarning("Trying to retrieve HTML through browser for {url}", context.WebDirectory.Url);

		string browserHtml;

		// The browser handles one page at a time
		using (services.Session.Concurrency.Cap(ConcurrencyReasons.Browser, 1))
		{
			browserHtml = await services.Browser.GetHtmlAsync(context.WebDirectory.Url);
		}

		if (browserHtml is null || browserHtml == context.Html)
		{
			return StepResult.Continue;
		}

		return StepResult.Done(await HtmlListingParser.ParseAsync(services, context.WebDirectory, browserHtml));
	}

	private static StepResult IframeHint(ParseContext context)
	{
		WebDirectory parsed = context.Parsed;
		ParserServices services = context.Services;

		if (parsed.Subdirectories.Count == 0 && parsed.Files.Count == 0 &&
			services.Http is not null &&
			services.Session.BrowserAttempts.Count == 0 &&
			context.Document.QuerySelector("iframe") != null)
		{
			IEnumerable<Uri> iframeUrls = context.Document.QuerySelectorAll<IHtmlInlineFrameElement>("iframe").Select(x => new Uri(services.Session.Root.Uri, x.GetAttribute("src")));

			context.Logger.LogWarning("No directories and files found on {url}, but did find <iframe> tag(s), you could try this url(s):\n{iframeUrls}", context.WebDirectory.Url, string.Join(Environment.NewLine, iframeUrls));
		}

		return StepResult.Done(parsed);
	}

	private static void RemoveAll(IHtmlCollection<IElement> elements)
	{
		foreach (IElement element in elements.ToList())
		{
			element.Remove();
		}
	}

	[GeneratedRegex(@"app\..*\.js")]
	private static partial Regex FileBrowserScriptRegex();

	[GeneratedRegex(@"""BaseURL"":""(?<BaseUrl>.*?)"",")]
	private static partial Regex FileBrowserBaseUrlRegex();
}
