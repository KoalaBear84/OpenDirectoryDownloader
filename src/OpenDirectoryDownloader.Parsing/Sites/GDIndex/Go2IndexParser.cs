using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using System.Net;
using System.Text.Json;

namespace OpenDirectoryDownloader.Parsing.Sites.GDIndex.Go2Index;

public static class Go2IndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "Go2Index";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory)
	{
		try
		{
			services.Session.SiteState.Get<GoogleDriveIndexState>().IsGoogleDriveIndex = true;

			if (!services.Session.SiteState.Get<IndexPasswordState>().IsKnown)
			{
				services.Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				services.Logger.LogInformation("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				services.Console.WriteLine("Check if password is needed...");
				services.Logger.LogInformation("Check if password is needed...");
				services.Session.SiteState.Get<IndexPasswordState>().Set(null);

				HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(webDirectory.Uri, new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
				{
					{ "page_index", 0 },
					{ "page_token", null },
					{ "password", services.Session.SiteState.Get<IndexPasswordState>().Password },
					{ "q", "" }
				})));

				Go2IndexResponse indexResponse = null;

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
					indexResponse = Go2IndexResponse.FromJson(responseJson);

					if (indexResponse.Error?.Code == (int)HttpStatusCode.Unauthorized)
					{
						services.Logger.LogInformation("Directory is password protected, please enter password.");

						services.Session.SiteState.Get<IndexPasswordState>().Set(services.Console.Ask("Directory is password protected, please enter password:"));

						services.Console.WriteLine($"Using password: {services.Session.SiteState.Get<IndexPasswordState>().Password}");
						services.Logger.LogInformation("Using password: {password}", services.Session.SiteState.Get<IndexPasswordState>().Password);

						httpResponseMessage = await services.Http.PostAsync(webDirectory.Uri, new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
						{
							{ "page_index", 0 },
							{ "page_token", null },
							{ "password", services.Session.SiteState.Get<IndexPasswordState>().Password },
							{ "q", "" }
						})));

						if (httpResponseMessage.IsSuccessStatusCode)
						{
							responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
							indexResponse = Go2IndexResponse.FromJson(responseJson);
						}
					}
				}

				if (indexResponse is null)
				{
					services.Console.WriteLine("Error. Invalid response. Stopping.");
					services.Logger.LogError("Error. Invalid response. Stopping.");
				}
				else
				{
					if (indexResponse.Error == null)
					{
						services.Console.WriteLine("Password OK!");
						services.Logger.LogInformation("Password OK!");

						webDirectory = await ScanIndexAsync(services, webDirectory);
					}
					else
					{
						services.Session.SiteState.Get<IndexPasswordState>().Clear();
						services.Console.WriteLine($"Error. Code: {indexResponse.Error.Code}, Message: {indexResponse.Error.Message}. Stopping.");
						services.Logger.LogError("Error. Code: {errorCode}, Message: {errorMessage}. Stopping.", indexResponse.Error.Code, indexResponse.Error.Message);
					}
				}
			}
			else
			{
				webDirectory = await ScanIndexAsync(services, webDirectory);
			}
		}
		catch (Exception ex)
		{
			services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.AddDelay(TimeSpan.FromSeconds(5));
			services.Logger.LogError(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanIndexAsync(ParserServices services, WebDirectory webDirectory)
	{
		webDirectory.Parser = Parser;

		try
		{
			RetryPolicy asyncRetryPolicy = RetryPolicy.Create((ex, waitTimeSpan, retry) =>
			{
				services.Logger.LogWarning("Error retrieving directory listing for {url}, waiting {waitTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
				services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.AddDelay(waitTimeSpan);
			}, 8);

			if (!webDirectory.Url.EndsWith('/'))
			{
				webDirectory.Url += '/';
			}

			long pageIndex = 0;
			string nextPageToken = null;

			do
			{
				await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
				{
					await services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.RateLimit();

					services.Logger.LogWarning("Retrieving listings for {relativeUrl}, page {page} with password: {password}", webDirectory.Uri.PathAndQuery, pageIndex + 1, services.Session.SiteState.Get<IndexPasswordState>().Password);

					HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(webDirectory.Uri, new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
					{
						{ "page_index", pageIndex },
						{ "page_token", nextPageToken },
						{ "password", services.Session.SiteState.Get<IndexPasswordState>().Password },
						{ "q", "" }
					})));

					webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
					httpResponseMessage.EnsureSuccessStatusCode();

					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

					Go2IndexResponse indexResponse = Go2IndexResponse.FromJson(responseJson);

					webDirectory.ParsedSuccessfully = indexResponse.Error == null;

					if (indexResponse.Error != null)
					{
						throw new Exception($"{indexResponse.Error.Code} | {indexResponse.Error.Message}");
					}

					if (indexResponse.Data?.Error != null)
					{
						if (indexResponse.Data.Error?.Message == "Rate Limit Exceeded")
						{
							throw new Exception("Rate limit exceeded");
						}
						else
						{
							throw new Exception($"Error in response: {indexResponse.Data.Error?.Code} | {indexResponse.Data.Error?.Message}");
						}
					}

					nextPageToken = indexResponse.NextPageToken;
					pageIndex = indexResponse.CurPageIndex + 1;

					foreach (File file in indexResponse.Data.Files)
					{
						if (file.MimeType == FolderMimeType)
						{
							webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
							{
								Parser = Parser,
								// Yes, string concatenation, do not use new Uri(webDirectory.Uri, file.Name), because things could end with a space...
								Url = $"{webDirectory.Uri}{Uri.EscapeDataString(file.Name)}/",
								Name = file.Name
							});
						}
						else
						{
							webDirectory.Files.Add(new WebFile
							{
								Url = new Uri(webDirectory.Uri, Uri.EscapeDataString(file.Name)).ToString(),
								FileName = file.Name,
								FileSize = file.Size,
								Description = GoogleDriveIndexMapping.GetFriendlyMimeTypeName(file.MimeType)
							});
						}
					}
				});
			} while (!string.IsNullOrWhiteSpace(nextPageToken));
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return webDirectory;
	}
}
