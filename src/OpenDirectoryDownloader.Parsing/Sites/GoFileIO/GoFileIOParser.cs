using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.GoFileIO;

public static class GoFileIOParser
{
	private static readonly Regex FolderHashRegex = new(@".*?\/d\/(?<FolderHash>.*)");
	private const string Parser = "GoFileIO";
	private const string StatusOK = "ok";
	private const string ApiBaseAddress = "https://api.gofile.io";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory)
	{
		try
		{
			string driveHash = GetDriveHash(webDirectory);

			if (services.Session.SiteState.Get<GoFileState>().AccountToken is null)
			{
				services.Console.WriteLine($"{Parser} creating temporary account.");
				services.Logger.LogInformation("{parser} creating temporary account.", Parser);

				HttpResponseMessage httpResponseMessage = await services.Http.GetAsync($"{ApiBaseAddress}/createAccount");

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					GoFileIOListingResult response = GoFileIOListingResult.FromJson(responseJson);

					if (response.Status != StatusOK)
					{
						throw new Exception($"Error creating account: {response.Status}");
					}

					services.Session.SiteState.Get<GoFileState>().AccountToken = response.Data.Token;

					webDirectory = await ScanAsync(services, webDirectory);
				}
			}
			else
			{
				webDirectory = await ScanAsync(services, webDirectory);
			}
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

	private static string GetDriveHash(WebDirectory webDirectory)
	{
		Match driveHashRegexMatch = FolderHashRegex.Match(webDirectory.Url);

		if (!driveHashRegexMatch.Success)
		{
			throw new Exception("Error getting drivehash");
		}

		return driveHashRegexMatch.Groups["FolderHash"].Value;
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
	{
		services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			string driveHash = GetDriveHash(webDirectory);

			services.Logger.LogWarning("Retrieving listings for {url}", webDirectory.Uri);

			HttpResponseMessage httpResponseMessage = await services.Http.GetAsync(GetApiListingUrl(driveHash, services.Session.SiteState.Get<GoFileState>().AccountToken));

			webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
			httpResponseMessage.EnsureSuccessStatusCode();

			string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

			GoFileIOListingResult indexResponse = GoFileIOListingResult.FromJson(responseJson);

			if (indexResponse.Status != StatusOK)
			{
				throw new Exception($"Error retrieving listing for {webDirectory.Uri}. Error: {indexResponse.Status}");
			}

			foreach (Content entry in indexResponse.Data.Contents.Values)
			{
				if (entry.Type == "folder")
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = GetFolderUrl(entry.Id),
						Name = entry.Name
					});
				}
				else
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = entry.DirectLink.ToString(),
						FileName = entry.Name,
						FileSize = entry.Size
					});
				}
			}
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return webDirectory;
	}

	private static string GetFolderUrl(string driveHash) => $"https://gofile.io/d/{driveHash}";
	private static string GetApiListingUrl(string driveHash, string accountToken) => $"{ApiBaseAddress}/getContent?contentId={Uri.EscapeDataString(driveHash)}&token={Uri.EscapeDataString(accountToken)}&websiteToken=12345";
}

/// <summary>The temporary account GoFile needs for its API</summary>
public sealed class GoFileState
{
	public string AccountToken { get; set; }
}
