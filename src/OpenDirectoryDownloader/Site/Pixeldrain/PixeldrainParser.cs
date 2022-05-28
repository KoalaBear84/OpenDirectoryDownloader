using NLog;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.Pixeldrain.FileResult;
using OpenDirectoryDownloader.Site.Pixeldrain.ListResult;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.Pixeldrain;

public static class PixeldrainParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static readonly Regex ListingTypeRegex = new Regex(@".*\/(?<ListingType>.*)\/.*");
	private static readonly Regex ListingRegex = new Regex(@"window\.viewer_data = (?<Listing>.*);");
	private const string Parser = "Pixeldrain";
	private const string ListingTypeFile = "u";
	private const string ListingTypeList = "l";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, string html)
	{
		try
		{
			webDirectory = await ScanAsync(httpClient, webDirectory, html);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error parsing {Parser} for URL: {webDirectory.Url}");
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

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory, string html)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

		webDirectory.Parser = Parser;

		try
		{
			Match listingTypeRegexMatch = ListingTypeRegex.Match(webDirectory.Url);

			if (!listingTypeRegexMatch.Success)
			{
				throw new Exception("Error determining listing type");
			}

			string listingType = listingTypeRegexMatch.Groups["ListingType"].Value;

			Logger.Warn($"Retrieving listings for {webDirectory.Uri}");

			Match listingRegexMatch = ListingRegex.Match(html);

			webDirectory.ParsedSuccessfully = listingRegexMatch.Success;

			if (!listingRegexMatch.Success)
			{
				throw new Exception("Listing not found");
			}

			string responseJson = listingRegexMatch.Groups["Listing"].Value;

			if (listingType == ListingTypeList)
			{
				PixeldrainListResult indexResponse = PixeldrainListResult.FromJson(responseJson);

				foreach (File file in indexResponse.ApiResponse.Files)
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = $"https://pixeldrain.com/api/file/{Uri.EscapeDataString(file.Id)}?download",
						FileName = file.Name,
						FileSize = file.Size
					});
				}
			}
			else if (listingType == ListingTypeFile)
			{
				PixeldrainFileResult indexResponse = PixeldrainFileResult.FromJson(responseJson);

				webDirectory.Files.Add(new WebFile
				{
					Url = $"https://pixeldrain.com/api/file/{Uri.EscapeDataString(indexResponse.ApiResponse.Id)}?download",
					FileName = indexResponse.ApiResponse.Name,
					FileSize = indexResponse.ApiResponse.Size
				});
			}
			else
			{
				throw new Exception($"Unknown listing type '{listingType}'");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error processing {Parser} for URL: {webDirectory.Url}");
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			//throw;
		}

		return await Task.FromResult(webDirectory);
	}
}
