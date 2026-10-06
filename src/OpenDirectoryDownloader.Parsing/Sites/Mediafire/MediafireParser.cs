using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.Mediafire;

public static class MediafireParser
{
	private static readonly Regex FolderIdRegex = new(@"\/folder\/(?<FolderId>[^/]*)(?:\/?.*)?");
	private static readonly Regex FolderIdRegex2 = new(@"\/\?(?<FolderId>[^/]*)(?:\/?.*)?");
	private const string Parser = "Mediafire";
	private const string StatusSuccess = "Success";
	private const string ApiBaseAddress = "https://www.mediafire.com/api/1.4";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory)
	{
		try
		{
			webDirectory = await ScanAsync(services, webDirectory);
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

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
	{
		services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Url);

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
					services.Logger.LogWarning("Retrieving {listingType} listing for {url}, page {page}", listingType, webDirectory.Url, chunkNumber);

					HttpResponseMessage httpResponseMessage = await services.Http.GetAsync(GetApiListingUrl(folderId, listingType, chunkNumber));

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
			services.Logger.LogError(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

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
