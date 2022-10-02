using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.CrushFtp;

public static class CrushFtpParser
{
	private const string Parser = "CrushFTP";
	private static string Authentication;
	private static string FunctionUrl;
	private static readonly Random Random = new();
	private static bool WarningShown = false;
	private static readonly RateLimiter RateLimiter = new(1, TimeSpan.FromSeconds(1), margin: 1);

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		if (!WarningShown)
		{
			WarningShown = true;

			Program.Logger.Warning("{parser} scanning is limited to {maxRequestsPerTimeSpan} directories per {seconds:F1} second(s)!", Parser, RateLimiter.MaxRequestsPerTimeSpan, RateLimiter.TimeSpan.TotalSeconds);
		}

		try
		{
			if (string.IsNullOrWhiteSpace(Authentication))
			{
				Authentication = OpenDirectoryIndexer.CookieContainer.GetAllCookies().FirstOrDefault(c => c.Name == "currentAuth")?.Value;
				FunctionUrl = $"{webDirectory.Uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port, UriFormat.UriEscaped)}/WebInterface/function/";
			}

			webDirectory = await ScanAsync(httpClient, webDirectory);
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error parsing {parser} for URL: {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Program.Logger.Debug("Retrieving listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			await RateLimiter.RateLimit();

			Dictionary<string, string> postValues = new()
			{
				{ "command", "getXMLListing" },
				{ "format", "JSONOBJ" },
				{ "path", $"{Uri.EscapeDataString(webDirectory.Uri.LocalPath)}%2F" },
				{ "random", Random.NextDouble().ToString() },
				{ "c2f", Authentication }
			};

			HttpResponseMessage httpResponseMessage = await httpClient.PostAsync(FunctionUrl, new FormUrlEncodedContent(postValues));

			httpResponseMessage.EnsureSuccessStatusCode();

			string response = await httpResponseMessage.Content.ReadAsStringAsync();

			CrushFtpResult crushFtpResult = CrushFtpResult.FromJson(response);

			foreach (Listing listing in crushFtpResult.Listing)
			{
				if (listing.Type == "DIR")
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = new Uri(webDirectory.Uri, listing.HrefPath).ToString(),
						Name = listing.Name
					});
				}
				else
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = new Uri(webDirectory.Uri, listing.HrefPath).ToString(),
						FileName = Path.GetFileName(listing.HrefPath),
						FileSize = listing.Size
					});
				}
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error processing {parser} for URL: {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}
}
