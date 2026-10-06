using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.FileResult;
using OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.ListResult;
using System.Text.RegularExpressions;
using File = OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.ListResult.File;

namespace OpenDirectoryDownloader.Parsing.Sites.Pixeldrain;

public static class PixeldrainParser
{
	private static readonly Regex ListingTypeRegex = new(@".*\/(?<ListingType>.*)\/.*");
	private static readonly Regex ListingRegex = new(@"window\.viewer_data = (?<Listing>.*);");
	private const string Parser = "Pixeldrain";
	private const string ListingTypeFile = "u";
	private const string ListingTypeList = "l";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, string html)
	{
		try
		{
			webDirectory = await ScanAsync(services, webDirectory, html);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error parsing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory, string html)
	{
		services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Url);

		webDirectory.Parser = Parser;

		try
		{
			Match listingTypeRegexMatch = ListingTypeRegex.Match(webDirectory.Url);

			if (!listingTypeRegexMatch.Success)
			{
				throw new Exception("Error determining listing type");
			}

			string listingType = listingTypeRegexMatch.Groups["ListingType"].Value;

			services.Logger.LogWarning("Retrieving listings for {url}", webDirectory.Url);

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
			services.Logger.LogError(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return await Task.FromResult(webDirectory);
	}
}
