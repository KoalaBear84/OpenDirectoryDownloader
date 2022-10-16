using Newtonsoft.Json;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Site.GDIndex.GdIndex;

public static class GdIndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private static readonly Regex RootIdRegex = new(@"default_root_id: '(?<RootId>.*?)'");
	private const string Parser = "GdIndex";
	private static readonly RateLimiter RateLimiter = new(1, TimeSpan.FromSeconds(1));

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, string html)
	{
		try
		{
			OpenDirectoryIndexer.Session.GDIndex = true;

			string rootId = string.Empty;

			if (OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_GdIndex_RootId))
			{
				rootId = OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_GdIndex_RootId];
			}
			else
			{
				rootId = GetRootId(html);
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_GdIndex_RootId] = rootId;
			}

			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
			{
				Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				Program.Logger.Information("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				Console.WriteLine("Check if password is needed...");
				Program.Logger.Information("Check if password is needed...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = null;

				HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"{webDirectory.Uri}?rootId={rootId}", null);

				GdIndexResponse indexResponse = null;

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
					indexResponse = GdIndexResponse.FromJson(responseJson);

					if (indexResponse == null)
					{
						Console.WriteLine("Directory is password protected, please enter password:");
						Program.Logger.Information("Directory is password protected, please enter password.");

						OpenDirectoryIndexer.Session.Parameters["GoIndex_Password"] = Console.ReadLine();

						Console.WriteLine($"Using password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");
						Program.Logger.Information("Using password: {password}", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]);

						httpResponseMessage = await httpClient.PostAsync($"{webDirectory.Uri}?rootId={rootId}", new StringContent(JsonConvert.SerializeObject(new Dictionary<string, object>
						{
							{ "page_index", 0 },
							{ "page_token", null },
							{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
							{ "q", "" }
						})));

						if (httpResponseMessage.IsSuccessStatusCode)
						{
							responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
							indexResponse = GdIndexResponse.FromJson(responseJson);
						}
					}
				}

				if (indexResponse != null)
				{
					Console.WriteLine("Password OK!");
					Program.Logger.Information("Password OK!");

					webDirectory = await ScanIndexAsync(httpClient, webDirectory);
				}
				else
				{
					OpenDirectoryIndexer.Session.Parameters.Remove(Constants.Parameters_Password);
					Console.WriteLine("Error. Stopping.");
					Program.Logger.Error("Error. Stopping.");
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

	private static string GetRootId(string html)
	{
		Match rootIdRegexMatch = RootIdRegex.Match(html);

		if (!rootIdRegexMatch.Success)
		{
			return "root";
		}

		return rootIdRegexMatch.Groups["RootId"].Value;
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

			await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
			{
				await RateLimiter.RateLimit();

				if (!webDirectory.Url.EndsWith("/"))
				{
					webDirectory.Url += "/";
				}

				Program.Logger.Warning("Retrieving listings for {url}", webDirectory.Uri);

				HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"{OpenDirectoryIndexer.Session.Root.Url}{Uri.EscapeDataString(webDirectory.Url.Replace(OpenDirectoryIndexer.Session.Root.Url, string.Empty).TrimEnd('/'))}/?rootId={OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_GdIndex_RootId]}", null);

				webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
				httpResponseMessage.EnsureSuccessStatusCode();

				string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

				GdIndexResponse indexResponse = GdIndexResponse.FromJson(responseJson);

				webDirectory.ParsedSuccessfully = indexResponse != null;

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
