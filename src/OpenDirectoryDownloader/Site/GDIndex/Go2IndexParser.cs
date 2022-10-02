using Newtonsoft.Json;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GDIndex.Go2Index;

public static class Go2IndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "Go2Index";
	private static readonly RateLimiter RateLimiter = new(1, TimeSpan.FromSeconds(1));

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			OpenDirectoryIndexer.Session.GDIndex = true;

			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
			{
				Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				Program.Logger.Information("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				Console.WriteLine("Check if password is needed...");
				Program.Logger.Information("Check if password is needed...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = null;

				HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(webDirectory.Uri, new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
				{
					{ "page_index", 0 },
					{ "page_token", null },
					{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
					{ "q", "" }
				})));

				Go2IndexResponse indexResponse = null;

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
					indexResponse = Go2IndexResponse.FromJson(responseJson);

					if (indexResponse.Error?.Code == (int)HttpStatusCode.Unauthorized)
					{
						Console.WriteLine("Directory is password protected, please enter password:");
						Program.Logger.Information("Directory is password protected, please enter password.");

						OpenDirectoryIndexer.Session.Parameters["GoIndex_Password"] = Console.ReadLine();

						Console.WriteLine($"Using password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");
						Program.Logger.Information("Using password: {password}", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]);

						httpResponseMessage = await httpClient.PostAsync(webDirectory.Uri, new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
						{
							{ "page_index", 0 },
							{ "page_token", null },
							{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
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
					Console.WriteLine("Error. Invalid response. Stopping.");
					Program.Logger.Error("Error. Invalid response. Stopping.");
				}
				else
				{
					if (indexResponse.Error == null)
					{
						Console.WriteLine("Password OK!");
						Program.Logger.Information("Password OK!");

						webDirectory = await ScanIndexAsync(httpClient, webDirectory);
					}
					else
					{
						OpenDirectoryIndexer.Session.Parameters.Remove(Constants.Parameters_Password);
						Console.WriteLine($"Error. Code: {indexResponse.Error.Code}, Message: {indexResponse.Error.Message}. Stopping.");
						Program.Logger.Error("Error. Code: {errorCode}, Message: {errorMessage}. Stopping.", indexResponse.Error.Code, indexResponse.Error.Message);
					}
				}
			}
			else
			{
				webDirectory = await ScanIndexAsync(httpClient, webDirectory);
			}
		}
		catch (Exception ex)
		{
			RateLimiter.AddDelay(TimeSpan.FromSeconds(5));
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

	private static async Task<WebDirectory> ScanIndexAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		webDirectory.Parser = Parser;

		try
		{
			Polly.Retry.AsyncRetryPolicy asyncRetryPolicy = Library.GetAsyncRetryPolicy((ex, waitTimeSpan, retry, pollyContext) =>
			{
				Program.Logger.Warning("Error retrieving directory listing for {url}, waiting {waitTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
				RateLimiter.AddDelay(waitTimeSpan);
			}, 8);

			if (!webDirectory.Url.EndsWith("/"))
			{
				webDirectory.Url += "/";
			}

			long pageIndex = 0;
			string nextPageToken = null;

			do
			{
				await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
				{
					await RateLimiter.RateLimit();

					Program.Logger.Warning("Retrieving listings for {relativeUrl}, page {page} with password: {password}", webDirectory.Uri.PathAndQuery, pageIndex + 1, OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]);

					HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(webDirectory.Uri, new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
					{
						{ "page_index", pageIndex },
						{ "page_token", nextPageToken },
						{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
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
								FileSize = file.Size
							});
						}
					}
				});
			} while (!string.IsNullOrWhiteSpace(nextPageToken));
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

		return webDirectory;
	}
}
