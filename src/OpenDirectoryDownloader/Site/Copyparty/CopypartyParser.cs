using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Site.Copyparty;

public static class Copyparty
{
	private const string Parser = "Copyparty";
	private static readonly Regex JsListingRegex = new("ls0\\s?=\\s?(?<Listing>.*);$", RegexOptions.Multiline);

	public static async Task<WebDirectory> ParseIndex(string baseUrl, HttpClient httpClient, WebDirectory webDirectory, IHtmlDocument htmlDocument, string html)
	{
		try
		{
			webDirectory = await ScanAsync(baseUrl, httpClient, webDirectory, htmlDocument, html);
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			throw;
		}

		return webDirectory;
	}

	private static Task<WebDirectory> ScanAsync(string baseUrl, HttpClient httpClient, WebDirectory webDirectory, IHtmlDocument htmlDocument, string html)
	{
		Program.Logger.Debug("Processing listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			IElement table = htmlDocument.QuerySelector("table#files");

			IHtmlCollection<IElement> entries = table.QuerySelectorAll("tbody tr");

			if (entries.Any())
			{
				foreach (IElement entry in entries)
				{
					IHtmlAnchorElement link = entry.QuerySelector("td:nth-child(2) a") as IHtmlAnchorElement;
					IHtmlTableCellElement fileSize = entry.QuerySelector("td:nth-child(3)") as IHtmlTableCellElement;

					if (link is null)
					{
						continue;
					}

					bool isDirectory = link.TextContent.EndsWith('/');

					Library.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

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
			Program.Logger.Error(ex, "Error processing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

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

		foreach (Dir dir in copypartyListing.Dirs)
		{
			parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
			{
				Parser = Parser,
				Url = new Uri(baseUri, dir.Href).ToString(),
				Name = dir.Name.TrimEnd('/')
			});
		}

		foreach (Dir file in copypartyListing.Files)
		{
			parsedWebDirectory.Files.Add(new WebFile
			{
				Url = new Uri(baseUri, file.Href).ToString(),
				FileName = file.Name,
				FileSize = file.Sz
			});
		}

		parsedWebDirectory.ParsedSuccessfully = true;

		return parsedWebDirectory;
	}
}
