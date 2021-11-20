using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.GDIndex.GoIndex;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GDIndex.GoIndex;

public static class GoIndexParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "GoIndex";
	private static readonly RateLimiter RateLimiter = new RateLimiter(1, TimeSpan.FromSeconds(1));

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
			{
				Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				Logger.Info($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");

				Console.WriteLine("Check if password is needed...");
				Logger.Info("Check if password is needed...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = "null";

				HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(webDirectory.Uri, new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
				{
					{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] }
				})));

				GoIndexResponse indexResponse = null;

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
					indexResponse = GoIndexResponse.FromJson(responseJson);

					if (indexResponse.Error?.Code == (int)HttpStatusCode.Unauthorized)
					{
						Console.WriteLine("Directory is password protected, please enter password:");
						Logger.Info("Directory is password protected, please enter password.");

						OpenDirectoryIndexer.Session.Parameters["GoIndex_Password"] = Console.ReadLine();

						Console.WriteLine($"Using password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");
						Logger.Info($"Using password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");

						httpResponseMessage = await httpClient.PostAsync(webDirectory.Uri, new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
						{
							{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] }
						})));

						if (httpResponseMessage.IsSuccessStatusCode)
						{
							responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
							indexResponse = GoIndexResponse.FromJson(responseJson);
						}
					}
				}

				if (indexResponse is null)
				{
					Logger.Error("Error. Invalid response. Stopping.");
				}
				else
				{
					if (indexResponse.Error == null)
					{
						Logger.Warn("Password OK!");

						webDirectory = await ScanIndexAsync(httpClient, webDirectory);
					}
					else
					{
						OpenDirectoryIndexer.Session.Parameters.Remove(Constants.Parameters_Password);
						Logger.Error($"Error. Code: {indexResponse.Error.Code}, Message: {indexResponse.Error.Message}. Stopping.");
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
			Logger.Error(ex, $"Error parsing {Parser} for URL: {webDirectory.Url}");
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
				Logger.Warn($"Error retrieving directory listing for {webDirectory.Uri}, waiting {waitTimeSpan.TotalSeconds} seconds.. Error: {ex.Message}");
				RateLimiter.AddDelay(waitTimeSpan);
			}, 8);

			await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
			{
				await RateLimiter.RateLimit();

				if (!webDirectory.Url.EndsWith("/"))
				{
					webDirectory.Url += "/";
				}

				Logger.Warn($"Retrieving listings for {webDirectory.Uri.PathAndQuery}{(!string.IsNullOrWhiteSpace(OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]) ? $" with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}" : string.Empty)}");

				HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(webDirectory.Uri, new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
				{
					{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] }
				})));

				webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
				httpResponseMessage.EnsureSuccessStatusCode();

				string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

				GoIndexResponse indexResponse = GoIndexResponse.FromJson(responseJson);

				webDirectory.ParsedSuccessfully = indexResponse.Error == null;

				if (indexResponse.Error != null)
				{
					throw new Exception($"Error in response: {indexResponse.Error.Code} | {indexResponse.Error.Message}");
				}

				foreach (File file in indexResponse.Files)
				{
					if (file.MimeType == FolderMimeType)
					{
						webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
						{
							Parser = Parser,
							// Yes, string concatenation, do not use new Uri(webDirectory.Uri, file.Name), because things could end with a space...
							Url = $"{webDirectory.Uri}{file.Name}/",
							Name = file.Name
						});
					}
					else
					{
						webDirectory.Files.Add(new WebFile
						{
							Url = new Uri(webDirectory.Uri, file.Name).ToString(),
							FileName = file.Name,
							FileSize = file.Size
						});
					}
				}
			});
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error retrieving directory listing for {webDirectory.Url}");
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
