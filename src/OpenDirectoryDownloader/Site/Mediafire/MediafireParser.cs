using NLog;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.Mediafire;

public static class MediafireParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static readonly Regex FolderIdRegex = new(@"\/folder\/(?<FolderId>[^/]*)(?:\/?.*)?");
	private static readonly Regex FolderIdRegex2 = new(@"\/\?(?<FolderId>[^/]*)(?:\/?.*)?");
	private const string Parser = "Mediafire";
	private const string StatusSuccess = "Success";
	private const string ApiBaseAddress = "https://www.mediafire.com/api/1.4";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			webDirectory = await ScanAsync(httpClient, webDirectory);
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

	private static string GetFolderId(WebDirectory webDirectory)
	{
		Match folderIdRegexMatch = FolderIdRegex.Match(webDirectory.Url);

		if (folderIdRegexMatch.Success)
		{
			return folderIdRegexMatch.Groups["FolderId"].Value;
		}

		Match folderIdRegex2Match = FolderIdRegex2.Match(webDirectory.Url);

		if (folderIdRegex2Match.Success)
		{
			return folderIdRegex2Match.Groups["FolderId"].Value;
		}

		throw new Exception("Error getting folder id");
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

		webDirectory.Parser = Parser;

		try
		{
			string folderId = GetFolderId(webDirectory);

			foreach (string listingType in new string[2] { "folders", "files" })
			{
				bool moreChunks = false;
				int chunkNumber = 1;

				do
				{
					Logger.Warn($"Retrieving {listingType} listing for {webDirectory.Uri}, page {chunkNumber}");

					HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(GetApiListingUrl(folderId, listingType, chunkNumber));

					webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
					httpResponseMessage.EnsureSuccessStatusCode();

					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					MediafireResult indexResponse = MediafireResult.FromJson(responseJson);

					// Nice boolean value Mediafire..
					moreChunks = indexResponse.Response.FolderContent.MoreChunks == "yes";

					if (indexResponse.Response.Result != StatusSuccess)
					{
						throw new Exception($"Error retrieving {listingType} listing for {webDirectory.Uri}, page {chunkNumber}. Error: {indexResponse.Response.Result}");
					}

					ProcessListing(webDirectory, indexResponse);
					chunkNumber++;
				} while (moreChunks);
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

	private static void ProcessListing(WebDirectory webDirectory, MediafireResult indexResponse)
	{
		if (indexResponse.Response.FolderContent.Folders is not null)
		{
			foreach (Folder folder in indexResponse.Response.FolderContent.Folders)
			{
				webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
				{
					Parser = Parser,
					Url = GetFolderUrl(folder.Folderkey),
					Name = folder.Name
				});
			}
		}

		if (indexResponse.Response.FolderContent.Files is not null)
		{
			foreach (File file in indexResponse.Response.FolderContent.Files)
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = file.Links?.NormalDownload?.ToString(),
					FileName = file.Filename,
					FileSize = file.Size
				});
			}
		}
	}

	private static string GetFolderUrl(string folderId) => $"https://www.mediafire.com/folder/{folderId}";
	private static string GetApiListingUrl(string folderId, string type, int chunkNumber = 1) => $"{ApiBaseAddress}/folder/get_content.php?content_type={type}&filter=all&order_by=name&order_direction=asc&chunk={chunkNumber}&version=1.5&folder_key={folderId}&response_format=json";
}
