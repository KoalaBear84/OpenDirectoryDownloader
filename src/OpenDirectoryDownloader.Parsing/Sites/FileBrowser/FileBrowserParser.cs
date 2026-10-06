using Microsoft.Extensions.Logging;
using AngleSharp.Html.Dom;
using OpenDirectoryDownloader.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.FileBrowser;

public static class FileBrowserParser
{
	private const string Parser = "FileBrowser";
	private static readonly Regex BaseUrlRegex = new(@"""BaseURL"":""(?<BaseUrl>.*?)"",");
	private static readonly Regex ShareIdRegex = new(@"\/share\/(?<ShareId>[^\/]+)(?<SubPath>\/?.*)");

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

	private static async Task<WebDirectory> ScanAsync(string baseUrl, ParserServices services, WebDirectory webDirectory, IHtmlDocument htmlDocument, string html)
	{
		services.Logger.LogDebug("Processing listings for '{url}'", webDirectory.Uri);

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

				string json = await services.Http.GetStringAsync(contentsUrl);

				return ParseListing(json, webDirectory, fileBrowserBaseUrl, shareId);
			}

			return webDirectory;
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

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
