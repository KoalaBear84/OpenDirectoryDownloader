using NLog;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GoFileIO;

public static class GoFileIOParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static readonly Regex FolderHashRegex = new(@".*?\/d\/(?<FolderHash>.*)");
	private const string Parser = "GoFileIO";
	private const string StatusOK = "ok";
	private const string ApiBaseAddress = "https://api.gofile.io";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			string driveHash = GetDriveHash(webDirectory);

			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_GoFileIOAccountToken))
			{
				Console.WriteLine($"{Parser} creating temporary account.");
				Logger.Info($"{Parser} creating temporary account.");

				HttpResponseMessage httpResponseMessage = await httpClient.GetAsync($"{ApiBaseAddress}/createAccount");

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					GoFileIOListingResult response = GoFileIOListingResult.FromJson(responseJson);

					if (response.Status != StatusOK)
					{
						throw new Exception($"Error creating account: {response.Status}");
					}

					OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_GoFileIOAccountToken] = response.Data.Token;

					webDirectory = await ScanAsync(httpClient, webDirectory);
				}
			}
			else
			{
				webDirectory = await ScanAsync(httpClient, webDirectory);
			}
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

	private static string GetDriveHash(WebDirectory webDirectory)
	{
		Match driveHashRegexMatch = FolderHashRegex.Match(webDirectory.Url);

		if (!driveHashRegexMatch.Success)
		{
			throw new Exception("Error getting drivehash");
		}

		return driveHashRegexMatch.Groups["FolderHash"].Value;
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

		webDirectory.Parser = Parser;

		try
		{
			string driveHash = GetDriveHash(webDirectory);

			Logger.Warn($"Retrieving listings for {webDirectory.Uri}");

			HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(GetApiListingUrl(driveHash, OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_GoFileIOAccountToken]));

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
			Logger.Error(ex, $"Error processing {Parser} for URL: {webDirectory.Url}");
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

	private static string GetFolderUrl(string driveHash) => $"https://gofile.io/d/{driveHash}";
	private static string GetApiListingUrl(string driveHash, string accountToken) => $"{ApiBaseAddress}/getContent?contentId={Uri.EscapeDataString(driveHash)}&token={Uri.EscapeDataString(accountToken)}&websiteToken=12345";
}
