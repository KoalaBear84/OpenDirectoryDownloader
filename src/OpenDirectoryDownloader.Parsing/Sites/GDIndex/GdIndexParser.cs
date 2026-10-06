using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.GDIndex.GdIndex;

public static class GdIndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private static readonly Regex RootIdRegex = new(@"default_root_id: '(?<RootId>.*?)'");
	private const string Parser = "GdIndex";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, string html)
	{
		try
		{
			services.Session.SiteState.Get<GoogleDriveIndexState>().IsGoogleDriveIndex = true;

			string rootId = string.Empty;

			if (services.Session.SiteState.Get<GoogleDriveIndexState>().GdIndexRootId is not null)
			{
				rootId = services.Session.SiteState.Get<GoogleDriveIndexState>().GdIndexRootId;
			}
			else
			{
				rootId = GetRootId(html);
				services.Session.SiteState.Get<GoogleDriveIndexState>().GdIndexRootId = rootId;
			}

			if (!services.Session.SiteState.Get<IndexPasswordState>().IsKnown)
			{
				services.Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				services.Logger.LogInformation("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				services.Console.WriteLine("Check if password is needed...");
				services.Logger.LogInformation("Check if password is needed...");
				services.Session.SiteState.Get<IndexPasswordState>().Set(null);

				HttpResponseMessage httpResponseMessage = await services.Http.PostAsync($"{webDirectory.Uri}?rootId={rootId}", null);

				GdIndexResponse indexResponse = null;

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
					indexResponse = GdIndexResponse.FromJson(responseJson);

					if (indexResponse == null)
					{
						services.Logger.LogInformation("Directory is password protected, please enter password.");

						services.Session.SiteState.Get<IndexPasswordState>().Set(services.Console.Ask("Directory is password protected, please enter password:"));

						services.Console.WriteLine($"Using password: {services.Session.SiteState.Get<IndexPasswordState>().Password}");
						services.Logger.LogInformation("Using password: {password}", services.Session.SiteState.Get<IndexPasswordState>().Password);

						httpResponseMessage = await services.Http.PostAsync($"{webDirectory.Uri}?rootId={rootId}", new StringContent(new JsonObject
						{
							["page_index"] = 0,
							["page_token"] = null,
							["password"] = services.Session.SiteState.Get<IndexPasswordState>().Password,
							["q"] = ""
						}.ToJsonString()));

						if (httpResponseMessage.IsSuccessStatusCode)
						{
							responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
							indexResponse = GdIndexResponse.FromJson(responseJson);
						}
					}
				}

				if (indexResponse != null)
				{
					services.Console.WriteLine("Password OK!");
					services.Logger.LogInformation("Password OK!");

					webDirectory = await ScanIndexAsync(services, webDirectory);
				}
				else
				{
					services.Session.SiteState.Get<IndexPasswordState>().Clear();
					services.Console.WriteLine("Error. Stopping.");
					services.Logger.LogError("Error. Stopping.");
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

	private static string GetRootId(string html)
	{
		Match rootIdRegexMatch = RootIdRegex.Match(html);

		if (!rootIdRegexMatch.Success)
		{
			return "root";
		}

		return rootIdRegexMatch.Groups["RootId"].Value;
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

			await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
			{
				await services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.RateLimit();

				if (!webDirectory.Url.EndsWith("/"))
				{
					webDirectory.Url += "/";
				}

				services.Logger.LogWarning("Retrieving listings for {url}", webDirectory.Uri);

				HttpResponseMessage httpResponseMessage = await services.Http.PostAsync($"{services.Session.Root.Url}{Uri.EscapeDataString(webDirectory.Url.Replace(services.Session.Root.Url, string.Empty).TrimEnd('/'))}/?rootId={services.Session.SiteState.Get<GoogleDriveIndexState>().GdIndexRootId}", null);

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
							// The name itself must still be escaped (Uri.EscapeDataString preserves a trailing
							// space as %20, so this doesn't lose the trailing-space handling above) - Drive
							// names are unconstrained and can contain '/', '%', '#', etc., which otherwise
							// corrupt this URL (wrong path segments, or a decodeURIComponent() throw later).
							Url = $"{webDirectory.Uri}{Uri.EscapeDataString(file.Name)}/",
							Name = file.Name
						});
					}
					else
					{
						webDirectory.Files.Add(new WebFile
						{
							Url = new Uri(webDirectory.Uri, file.Name).ToString(),
							FileName = file.Name,
							FileSize = file.Size,
							Description = GoogleDriveIndexMapping.GetFriendlyMimeTypeName(file.MimeType)
						});
					}
				}
			});
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
