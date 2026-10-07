using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using System.Net;

namespace OpenDirectoryDownloader.Parsing.Listings;

/// <summary>
/// The "SNT index generator" of Studenten Net Twente (https://ftp.snt.utwente.nl/): the directories are in a
/// &lt;nav&gt; sidebar, the files in a table whose size cell has the exact byte count as title.
/// </summary>
internal static class SntListingParser
{
	public const string Parser = "ParseSntListing";

	public static bool IsSntListing(IDocument document)
	{
		return document.QuerySelector<IHtmlMetaElement>("meta[name=generator]")?.Content == "SNT index generator";
	}

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IDocument document, bool checkParents, ILogger logger)
	{
		foreach (IElement link in document.QuerySelectorAll("nav a.dir"))
		{
			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
			{
				Parser = Parser,
				Url = fullUrl,
				Name = WebUtility.HtmlDecode(link.TextContent.Trim()).TrimEnd('/')
			});
		}

		foreach (IElement row in document.QuerySelectorAll("table.listing tbody tr"))
		{
			IElement link = row.QuerySelector("td.name.file a");

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			// title="10000000 bytes"
			string sizeTitle = row.QuerySelector("td.size")?.GetAttribute("title");
			string sizeDigits = sizeTitle is null ? null : new string([.. sizeTitle.Where(char.IsDigit)]);

			parsedWebDirectory.Files.Add(new WebFile
			{
				Url = fullUrl,
				FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
				FileSize = long.TryParse(sizeDigits, out long size) ? size : null
			});
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}
