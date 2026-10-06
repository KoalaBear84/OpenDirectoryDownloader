using Microsoft.Extensions.Logging;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Listings;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.Copyparty;

public static class Copyparty
{
	private const string Parser = "Copyparty";
	private static readonly Regex JsListingRegex = new("ls0\\s?=\\s?(?<Listing>.*);$", RegexOptions.Multiline);

	public static async Task<WebDirectory> ParseIndex(string baseUrl, ParserServices services, WebDirectory webDirectory, IHtmlDocument htmlDocument, string html)
	{
		try
		{
			webDirectory = await ScanAsync(baseUrl, services, webDirectory, htmlDocument, html);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			throw;
		}

		return webDirectory;
	}

	private static Task<WebDirectory> ScanAsync(string baseUrl, ParserServices services, WebDirectory webDirectory, IHtmlDocument htmlDocument, string html)
	{
		services.Logger.LogDebug("Processing listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			IElement table = htmlDocument.QuerySelector("table#files");

			IHtmlCollection<IElement> entries = table.QuerySelectorAll("tbody tr");

			if (entries.Length != 0)
			{
				foreach (IElement entry in entries)
				{
					IHtmlTableCellElement fileSize = entry.QuerySelector("td:nth-child(3)") as IHtmlTableCellElement;

					if (entry.QuerySelector("td:nth-child(2) a") is not IHtmlAnchorElement link)
					{
						continue;
					}

					bool isDirectory = link.TextContent.EndsWith('/');

					ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

					if (isDirectory)
					{
						string directoryName = link.TextContent.TrimEnd('/');

						webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
						{
							Parser = Parser,
							Url = fullUrl,
							Name = directoryName
						});
					}
					else
					{
						webDirectory.Files.Add(new WebFile
						{
							Url = fullUrl,
							FileName = Path.GetFileName(WebUtility.UrlDecode(fullUrl.Split('?')[0])),
							FileSize = FileSizeHelper.ParseFileSize(fileSize.TextContent)
						});
					}
				}

				webDirectory.ParsedSuccessfully = true;
			}
			else
			{
				return Task.FromResult(ParseCopypartyJavaScriptListing(baseUrl, webDirectory, htmlDocument, html));
			}

			return Task.FromResult(webDirectory);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return Task.FromResult(webDirectory);
	}

	private static WebDirectory ParseCopypartyJavaScriptListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, string html)
	{
		Match jsListingRegexMatch = JsListingRegex.Match(html);

		if (!jsListingRegexMatch.Success)
		{
			return parsedWebDirectory;
		}

		CopypartyListing copypartyListing = CopypartyListing.FromJson(jsListingRegexMatch.Groups["Listing"].Value);

		Uri baseUri = new(baseUrl);

		foreach (Dir dir in copypartyListing?.Dirs ?? [])
		{
			parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
			{
				Parser = Parser,
				Url = new Uri(baseUri, dir.Href).ToString(),
				Name = (dir.Name ?? WebUtility.UrlDecode(dir.Href)).TrimEnd('/')
			});
		}

		foreach (Dir file in copypartyListing?.Files ?? [])
		{
			parsedWebDirectory.Files.Add(new WebFile
			{
				Url = new Uri(baseUri, file.Href).ToString(),
				FileName = file.Name ?? Path.GetFileName(WebUtility.UrlDecode(file.Href.Split('?')[0])),
				FileSize = file.Sz
			});
		}

		parsedWebDirectory.ParsedSuccessfully = true;

		return parsedWebDirectory;
	}
}
