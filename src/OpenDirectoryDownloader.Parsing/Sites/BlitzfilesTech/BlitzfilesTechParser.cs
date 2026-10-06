using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.BlitzfilesTech;

/// <summary>
/// Similar to GoIndex
/// </summary>
public static class BlitzfilesTechParser
{
	private static readonly Regex DriveHashRegex = new(@"\/drive\/s\/(?<DriveHash>.*)");
	private const string Parser = "BlitzfilesTech";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory)
	{
		try
		{
			string driveHash = GetDriveHash(webDirectory);

			if (!services.Session.SiteState.Get<IndexPasswordState>().IsKnown)
			{
				services.Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				services.Logger.LogInformation("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				services.Console.WriteLine("Check if password is needed (unsupported currently)...");
				services.Logger.LogInformation("Check if password is needed (unsupported currently)...");
				services.Session.SiteState.Get<IndexPasswordState>().Set(string.Empty);

				HttpResponseMessage httpResponseMessage = await services.Http.GetAsync(GetFolderUrl(driveHash, string.Empty, 0));

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					BlitzfilesTechResponse response = BlitzfilesTechResponse.FromJson(responseJson);

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
			services.Logger.LogError(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

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

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
	{
		services.Logger.LogDebug("Retrieving listings for '{url}' with password: {password}", webDirectory.Uri, services.Session.SiteState.Get<IndexPasswordState>().Password);

		webDirectory.Parser = Parser;

		try
		{
			await services.Session.SiteState.Get<BlitzfilesTechState>().RateLimiter.RateLimit();

			string driveHash = GetDriveHash(webDirectory);
			string entryHash = string.Empty;
			long pageIndex = 0;
			long totalPages = 0;

			do
			{
				services.Logger.LogWarning("Retrieving listings for '{url}' with password: {password}, page {pageIndex + 1}", webDirectory.Uri, services.Session.SiteState.Get<IndexPasswordState>().Password);

				HttpResponseMessage httpResponseMessage = await services.Http.GetAsync(GetFolderUrl(driveHash, entryHash, pageIndex));

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
			services.Logger.LogError(ex, "Error processing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return webDirectory;
	}

	private static string GetFolderUrl(string driveHash, string entryHash, long pageIndex)
	{
		return $"https://blitzfiles.tech/files/secure/drive/shareable-links/{driveHash}{(!string.IsNullOrWhiteSpace(entryHash) ? $":{entryHash}" : string.Empty)}?page={pageIndex + 1}&order=updated_at:desc&withEntries=true";
	}
}

public sealed class BlitzfilesTechState
{
	public RateLimiter RateLimiter { get; } = new(1, TimeSpan.FromSeconds(1));
}
