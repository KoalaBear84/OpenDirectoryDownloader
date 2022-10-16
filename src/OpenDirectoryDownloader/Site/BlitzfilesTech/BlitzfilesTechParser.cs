using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Site.BlitzfilesTech;

/// <summary>
/// Similar to GoIndex
/// </summary>
public static class BlitzfilesTechParser
{
	private static readonly Regex DriveHashRegex = new(@"\/drive\/s\/(?<DriveHash>.*)");
	private const string Parser = "BlitzfilesTech";
	private static readonly RateLimiter RateLimiter = new(1, TimeSpan.FromSeconds(1));

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			string driveHash = GetDriveHash(webDirectory);

			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
			{
				Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				Program.Logger.Information("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				Console.WriteLine("Check if password is needed (unsupported currently)...");
				Program.Logger.Information("Check if password is needed (unsupported currently)...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = string.Empty;

				HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(GetFolderUrl(driveHash, string.Empty, 0));

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					BlitzfilesTechResponse response = BlitzfilesTechResponse.FromJson(responseJson);

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

	private static string GetDriveHash(WebDirectory webDirectory)
	{
		Match driveHashRegexMatch = DriveHashRegex.Match(webDirectory.Url);

		if (!driveHashRegexMatch.Success)
		{
			throw new Exception("Error getting drivehash");
		}

		return driveHashRegexMatch.Groups["DriveHash"].Value;
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Program.Logger.Debug("Retrieving listings for '{url}' with password: {password}", webDirectory.Uri, OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]);

		webDirectory.Parser = Parser;

		try
		{
			await RateLimiter.RateLimit();

			string driveHash = GetDriveHash(webDirectory);
			string entryHash = string.Empty;
			long pageIndex = 0;
			long totalPages = 0;

			do
			{
				Program.Logger.Warning("Retrieving listings for '{url}' with password: {password}, page {pageIndex + 1}", webDirectory.Uri, OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]);

				HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(GetFolderUrl(driveHash, entryHash, pageIndex));

				webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
				httpResponseMessage.EnsureSuccessStatusCode();

				string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

				BlitzfilesTechResponse indexResponse = BlitzfilesTechResponse.FromJson(responseJson);
				entryHash = indexResponse.Link.Entry.Hash;

				pageIndex = indexResponse.FolderChildren.CurrentPage;
				totalPages = indexResponse.FolderChildren.LastPage;

				foreach (Entry entry in indexResponse.FolderChildren.Data)
				{
					if (entry.Type == "folder")
					{
						webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
						{
							Parser = Parser,
							Url = $"https://blitzfiles.tech/files/drive/s/{driveHash}:{entry.Hash}",
							Name = entry.Name
						});
					}
					else
					{
						webDirectory.Files.Add(new WebFile
						{
							Url = $"https://blitzfiles.tech/files/secure/uploads/download?hashes={entry.Hash}&shareable_link={indexResponse.Link.Id}",
							FileName = entry.Name,
							FileSize = entry.FileSize
						});
					}
				}
			} while (pageIndex < totalPages);
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

	private static string GetFolderUrl(string driveHash, string entryHash, long pageIndex)
	{
		return $"https://blitzfiles.tech/files/secure/drive/shareable-links/{driveHash}{(!string.IsNullOrWhiteSpace(entryHash) ? $":{entryHash}" : string.Empty)}?page={pageIndex + 1}&order=updated_at:desc&withEntries=true";
	}
}
