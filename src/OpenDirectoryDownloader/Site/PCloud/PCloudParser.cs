using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.PCloud.ListResult;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Site.PCloud;

public static class PCloudParser
{
	private static readonly Regex ListingRegex = new(@"<script>\W*(?:var|let)\W*directLinkData=(?<Listing>.*);.*<\/script>", RegexOptions.Singleline);
	private const string Parser = "pCloud";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, string html)
	{
		try
		{
			webDirectory = await ScanAsync(httpClient, webDirectory, html);
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

    private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory, string html)
    {
        Program.Logger?.Debug("Retrieving listings for {url}", webDirectory.Url);

        webDirectory.Parser = Parser;

        try
        {
            Program.Logger?.Warning("Retrieving listings for {url}", webDirectory.Url);

            Match listingRegexMatch = ListingRegex.Match(html);

            webDirectory.ParsedSuccessfully = listingRegexMatch.Success;

            if (!listingRegexMatch.Success)
            {
                throw new Exception("Listing not found");
            }

            string responseJson = listingRegexMatch.Groups["Listing"].Value;

            PCloudListing indexResponse = PCloudListing.FromJson(responseJson);

            foreach (Content entry in indexResponse.Content)
            {
                if (entry.Icon == 20 || entry.Size is null)
                {
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                    {
						Parser = Parser,
						// Keep it like this, as some entries have a trailing space
						Url = $"{webDirectory.Uri}{entry.Urlencodedname}",
                        Name = entry.Name
                    });
                }
                else
                {
                    webDirectory.Files.Add(new WebFile
                    {
						// Keep it like this, as some entries have a trailing space
						Url = $"{webDirectory.Uri}{entry.Urlencodedname}",
						FileName = entry.Name,
                        FileSize = entry.Size
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger?.Error(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
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
