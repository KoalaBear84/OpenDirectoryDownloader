using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using System.Net;
using System.Text.Json;

namespace OpenDirectoryDownloader.Parsing.Sites.GDIndex.GoIndex;

public static class GoIndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "GoIndex";

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
				services.Session.SiteState.Get<IndexPasswordState>().Set("");

				HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(webDirectory.Uri, new StringContent(new JsonObject
				{
					["password"] = services.Session.SiteState.Get<IndexPasswordState>().Password
				}.ToJsonString()));

				GoIndexResponse indexResponse = null;

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
					indexResponse = GoIndexResponse.FromJson(responseJson);

					if (indexResponse.Error?.Code == (int)HttpStatusCode.Unauthorized)
					{
						services.Logger.LogInformation("Directory is password protected, please enter password.");

						services.Session.SiteState.Get<IndexPasswordState>().Set(services.Console.Ask("Directory is password protected, please enter password:"));

						services.Console.WriteLine($"Using password: {services.Session.SiteState.Get<IndexPasswordState>().Password}");
						services.Logger.LogInformation("Using password: {password}", services.Session.SiteState.Get<IndexPasswordState>().Password);

						httpResponseMessage = await services.Http.PostAsync(webDirectory.Uri, new StringContent(new JsonObject
						{
							["password"] = services.Session.SiteState.Get<IndexPasswordState>().Password
						}.ToJsonString()));

						if (httpResponseMessage.IsSuccessStatusCode)
						{
							responseJson = await httpResponseMessage.Content.ReadAsStringAsync();
							indexResponse = GoIndexResponse.FromJson(responseJson);
						}
					}
				}

				if (indexResponse is null)
				{
					services.Logger.LogError("Error. Invalid response. Stopping.");
				}
				else
				{
					if (indexResponse.Error == null)
					{
						services.Logger.LogWarning("Password OK!");

						webDirectory = await ScanIndexAsync(services, webDirectory);
					}
					else
					{
						services.Session.SiteState.Get<IndexPasswordState>().Clear();
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
			services.Logger.LogError(ex, "Error parsing {parser} for {url}", Parser, webDirectory.Url);
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
				services.Logger.LogWarning("Error retrieving directory listing for {url}, waiting {waiTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
				services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.AddDelay(waitTimeSpan);
			}, 8);

			await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
			{
				await services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.RateLimit();

				if (!webDirectory.Url.EndsWith("/"))
				{
					webDirectory.Url += "/";
				}

				services.Logger.LogWarning("Retrieving listings for {relativeUrl} with password: {password}", webDirectory.Uri.PathAndQuery, services.Session.SiteState.Get<IndexPasswordState>().Password);

				HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(webDirectory.Uri, new StringContent(new JsonObject
				{
					["password"] = services.Session.SiteState.Get<IndexPasswordState>().Password
				}.ToJsonString()));

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
