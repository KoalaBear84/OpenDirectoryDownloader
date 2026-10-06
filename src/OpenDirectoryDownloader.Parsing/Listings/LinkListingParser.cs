using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Listings;

/// <summary>The last resorts: every &lt;li&gt; or every &lt;a&gt; on the page, a link with an extension being a file and anything else a directory.</summary>
internal static partial class LinkListingParser
{
	public const string ListItemsParser = "ParseListItemsDirectoryListing";
	public const string LinksParser = "ParseLinksDirectoryListing";

	public static WebDirectory ParseListItems(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool checkParents, ILogger logger)
	{
		bool firstLink = true;

		Regex regex = RegexListItemSize();

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

			ProcessLink(baseUrl, parsedWebDirectory, link, ListItemsParser, regexMatch.Success ? regexMatch.Groups["Size"].Value : null);
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}

	public static WebDirectory ParseLinks(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> links, bool checkParents, ILogger logger)
	{
		foreach (IElement link in links)
		{
			ProcessLink(baseUrl, parsedWebDirectory, link, LinksParser);
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}

	private static void ProcessLink(string baseUrl, WebDirectory parsedWebDirectory, IElement link, string parser, string sizeHint = null)
	{
		if (!link.HasAttribute("href"))
		{
			return;
		}

		if (!ListingHelpers.IsValidLink(link))
		{
			return;
		}

		try
		{
			ListingHelpers.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

			fullUrl = ListingPostProcessor.StripUrl(fullUrl);

			UrlEncodingParser urlEncodingParser = new(fullUrl);

			if (uri.Segments.Length == 1 && uri.Segments.Last() == "/" && urlEncodingParser["dir"] == null &&
			    urlEncodingParser["path"] == null)
			{
				return;
			}

			if (baseUrl == ListingPostProcessor.StripUrl(fullUrl))
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

				string name;

				if (dirParam)
				{
					name = urlEncodingParser["dir"] != null
						? WebUtility.UrlDecode(new Uri(parsedWebDirectory.Uri, urlEncodingParser["dir"]).Segments.Last()).TrimEnd(ListingHelpers.TrimChars)
						: WebUtility.UrlDecode(new Uri(parsedWebDirectory.Uri, urlEncodingParser["path"]).Segments.Last()).TrimEnd(ListingHelpers.TrimChars);
				}
				else if (!directoryListAsp)
				{
					name = WebUtility.UrlDecode(uri.Segments.Last()).Trim().TrimEnd(ListingHelpers.TrimChars);
				}
				else
				{
					name = link.TextContent.Trim().TrimEnd(ListingHelpers.TrimChars);
				}

				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = parser,
					Url = fullUrl,
					Name = name
				});
			}
		}
		catch (Exception)
		{
			// Ignore link
		}
	}

	[GeneratedRegex(@"Size: (?<Size>\d+(\.\d+)? \S+)")]
	private static partial Regex RegexListItemSize();
}
