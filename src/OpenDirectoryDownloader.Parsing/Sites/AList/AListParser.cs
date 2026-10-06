using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Parsing.Sites.AList.ListResult;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenDirectoryDownloader.Parsing.Sites.AList;

public static class AListParser
{
	public const string Parser = "AList";

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

    private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
    {
        services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Url);

        webDirectory.Parser = Parser;

		try
		{
			RetryPolicy asyncRetryPolicy = RetryPolicy.Create((ex, waitTimeSpan, retry) =>
			{
				services.Logger.LogWarning("Error retrieving directory listing for {url}, waiting {waitTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
			}, 4);

			if (!webDirectory.Url.EndsWith('/'))
			{
				webDirectory.Url += '/';
			}

			long pageIndex = 1;
			long totalEntries = 0;

			do
			{
				await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
				{
                    string password = services.Session.SiteState.Get<IndexPasswordState>().Password;

                    if (services.Session.SiteState.Get<IndexPasswordState>().IsKnown)
                    {
                        services.Logger.LogWarning("Retrieving listings for {relativeUrl}, page {page} with password: {password}", WebUtility.UrlDecode(webDirectory.Uri.PathAndQuery), pageIndex, password);
                    }
                    else
                    {
                        services.Logger.LogWarning("Retrieving listings for {relativeUrl}, page {page} without password", WebUtility.UrlDecode(webDirectory.Uri.PathAndQuery), pageIndex);
                    }

					Uri apiFsListUri = new(webDirectory.Uri, "/api/fs/list");

					JsonObject postValues = new()
					{
						["path"] = webDirectory.Uri.LocalPath,
						["password"] = password,
						["page"] = pageIndex,
						["per_page"] = 0,
						["refresh"] = false
					};

                    HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(apiFsListUri, new StringContent(postValues.ToJsonString(), Encoding.UTF8, "application/json"));

					webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
					httpResponseMessage.EnsureSuccessStatusCode();

					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					AListListing listing = AListListing.FromJson(responseJson);

					webDirectory.ParsedSuccessfully = listing.Code == 200;

					if (!webDirectory.ParsedSuccessfully)
					{
						throw new Exception($"Listing returned HTTP {listing.Code} with message: {listing.Message}");
					}

					totalEntries = listing.Data.Total;

					if (totalEntries == 0)
					{
						return;
					}

					foreach (Content entry in listing.Data.Content)
					{
						if (entry.IsDir)
						{
							webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
							{
								Parser = Parser,
								Url = new Uri(webDirectory.Uri, entry.Name).ToString(),
								Name = entry.Name
							});
						}
						else
						{
							webDirectory.Files.Add(new WebFile
							{
								Url = new Uri(webDirectory.Uri, entry.Name).ToString(),
								FileName = Path.GetFileName(entry.Name),
								FileSize = entry.Size
							});
						}
					}

					pageIndex++;
				});
			} while ((webDirectory.Subdirectories.Count + webDirectory.Files.Count) < totalEntries);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

        return await Task.FromResult(webDirectory);
    }
}
