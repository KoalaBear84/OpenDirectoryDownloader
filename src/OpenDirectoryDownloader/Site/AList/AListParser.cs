using Acornima.Ast;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.AList.ListResult;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenDirectoryDownloader.Site.AList;

public static class AListParser
{
	public const string Parser = "AList";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			webDirectory = await ScanAsync(httpClient, webDirectory);
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

    private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
    {
        Program.Logger?.Debug("Retrieving listings for {url}", webDirectory.Url);

        webDirectory.Parser = Parser;

		try
		{
			Polly.Retry.AsyncRetryPolicy asyncRetryPolicy = Library.GetAsyncRetryPolicy((ex, waitTimeSpan, retry, pollyContext) =>
			{
				Program.Logger.Warning("Error retrieving directory listing for {url}, waiting {waitTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
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
                    if (OpenDirectoryIndexer.Session.Parameters.TryGetValue(Constants.Parameters_Password, out string password))
                    {
                        Program.Logger.Warning("Retrieving listings for {relativeUrl}, page {page} with password: {password}", WebUtility.UrlDecode(webDirectory.Uri.PathAndQuery), pageIndex, password);
                    }
                    else
                    {
                        Program.Logger.Warning("Retrieving listings for {relativeUrl}, page {page} without password", WebUtility.UrlDecode(webDirectory.Uri.PathAndQuery), pageIndex);
                    }

					Uri apiFsListUri = new(webDirectory.Uri, "/api/fs/list");

					Dictionary<string, object> postValues = new()
					{
						{ "path", webDirectory.Uri.LocalPath },
						{ "password", password },
						{ "page", pageIndex },
						{ "per_page", 0 },
						{ "refresh", false }
					};

                    HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(apiFsListUri, new StringContent(JsonSerializer.Serialize(postValues), Encoding.UTF8, "application/json"));

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
			Program.Logger.Error(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
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
