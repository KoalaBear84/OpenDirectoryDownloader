using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.AList.ListResult;
using OpenDirectoryDownloader.Site.Dufs;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenDirectoryDownloader.Site.AList;

public static class DufsParser
{
	public const string Parser = "Dufs";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, string dufsIndexDataContent)
	{
		try
		{
			Program.Logger?.Debug("Parsing Dufs index data for {url}", webDirectory.Url);

			DufsListing dufsListing = DufsListing.FromJson(dufsIndexDataContent);
			webDirectory.ParsedSuccessfully = dufsListing.DirExists;

			if (!webDirectory.ParsedSuccessfully)
			{
				throw new Exception($"Dufs index data indicates directory does not exist");
			}

			webDirectory = await ScanAsync(httpClient, webDirectory, dufsListing);
		}
		catch (Exception ex)
		{
			Program.Logger?.Error(ex, "Error parsing {parser} for {url}", Parser, webDirectory.Url);
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

    private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory, DufsListing dufsListing)
    {
        Program.Logger?.Debug("Retrieving listings for {url}", webDirectory.Url);

        webDirectory.Parser = Parser;

		try
		{
			// Walk throug the DufsListing and populate the WebDirectory
			foreach (Dufs.Path path in dufsListing.Paths)
			{
				if (path.PathType == "Dir")
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = new Uri(webDirectory.Uri, path.Name).ToString(),
						Name = path.Name
					});
				}
				else
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = new Uri(webDirectory.Uri, path.Name).ToString(),
						FileName = path.Name,
						FileSize = path.Size
					});
				}
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error processing directory listing for {url}", webDirectory.Url);
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
