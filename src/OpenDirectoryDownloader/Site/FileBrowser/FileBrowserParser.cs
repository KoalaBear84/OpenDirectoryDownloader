using AngleSharp.Html.Dom;
using OpenDirectoryDownloader.Shared.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Site.FileBrowser;

public static class FileBrowserParser
{
	private const string Parser = "FileBrowser";
	private static readonly Regex BaseUrlRegex = new(@"""BaseURL"":""(?<BaseUrl>.*?)"",");
	private static readonly Regex ShareIdRegex = new(@"\/share\/(?<ShareId>[^\/]+)(?<SubPath>\/?.*)");

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

	private static async Task<WebDirectory> ScanAsync(string baseUrl, HttpClient httpClient, WebDirectory webDirectory, IHtmlDocument htmlDocument, string html)
	{
		Program.Logger.Debug("Processing listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			Match baseUrlRegexMatch = BaseUrlRegex.Match(html);

			if (baseUrlRegexMatch.Success)
			{
				Match shareIdRegexMatch = ShareIdRegex.Match(webDirectory.Url);
				string shareId = shareIdRegexMatch.Groups["ShareId"].Value.TrimEnd('/');
				string subPath = shareIdRegexMatch.Groups["SubPath"].Value;

				Uri fileBrowserBaseUrl = new(new Uri(webDirectory.Uri.GetLeftPart(UriPartial.Authority)), $"{baseUrlRegexMatch.Groups["BaseUrl"].Value}/");

				string contentsUrl = $"{fileBrowserBaseUrl}api/public/share/{shareId}{subPath}";

				string json = await httpClient.GetStringAsync(contentsUrl);

				return ParseListing(json, webDirectory, fileBrowserBaseUrl, shareId);
			}

			return webDirectory;
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

		return webDirectory;
	}

	private static WebDirectory ParseListing(string json, WebDirectory parsedWebDirectory, Uri fileBrowserBaseUrl, string shareId)
	{
		FileBrowserResult fileBrowserResult = FileBrowserResult.FromJson(json);

		foreach (Item item in fileBrowserResult.Items)
		{
			if (item.IsDir)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = Parser,
					// Cannot use new Uri because it strips spaces at the end..
					Url = $"{fileBrowserBaseUrl}share/{shareId}/{Uri.EscapeDataString(item.Path.TrimStart('/'))}",
					Name = item.Name
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					// Cannot use new Uri because it strips spaces at the end..
					Url = $"{fileBrowserBaseUrl}api/public/dl/{shareId}{Uri.EscapeDataString(item.Path)}",
					FileName = item.Name,
					FileSize = item.Size
				});
			}
		}

		parsedWebDirectory.ParsedSuccessfully = true;

		return parsedWebDirectory;
	}
}
