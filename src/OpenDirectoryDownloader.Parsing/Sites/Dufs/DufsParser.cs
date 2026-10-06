using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites.AList.ListResult;
using OpenDirectoryDownloader.Parsing.Sites.Dufs;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenDirectoryDownloader.Parsing.Sites.Dufs;

public static class DufsParser
{
	public const string Parser = "Dufs";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, string dufsIndexDataContent)
	{
		try
		{
			services.Logger.LogDebug("Parsing Dufs index data for {url}", webDirectory.Url);

			DufsListing dufsListing = DufsListing.FromJson(dufsIndexDataContent);
			webDirectory.ParsedSuccessfully = dufsListing.DirExists;

			if (!webDirectory.ParsedSuccessfully)
			{
				throw new Exception($"Dufs index data indicates directory does not exist");
			}

			webDirectory = await ScanAsync(services, webDirectory, dufsListing);
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

    private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory, DufsListing dufsListing)
    {
        services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Url);

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
			services.Logger.LogError(ex, "Error processing directory listing for {url}", webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

        return await Task.FromResult(webDirectory);
    }
}
