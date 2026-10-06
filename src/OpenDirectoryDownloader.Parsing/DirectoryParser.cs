using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Html;
using OpenDirectoryDownloader.Parsing.Listings;

namespace OpenDirectoryDownloader.Parsing;

/// <summary>Parses a page without a scan around it: no network, no browser, no console. What the tests use.</summary>
public static class DirectoryParser
{
	public static Task<WebDirectory> ParseHtml(WebDirectory webDirectory, string html, bool checkParents = true)
	{
		return HtmlListingParser.ParseAsync(ParserServices.Offline(), webDirectory, html, checkParents: checkParents);
	}

	public static void CheckParsedResults(WebDirectory webDirectory, string baseUrl, bool checkParents)
	{
		ListingPostProcessor.CheckParsedResults(webDirectory, baseUrl, checkParents);
	}
}
