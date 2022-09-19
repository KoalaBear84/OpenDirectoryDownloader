using NLog;
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
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private const string Parser = "CrushFTP";
	private static string Authentication;
	//private static string Username;
	private static string FunctionUrl;
	private static readonly Random Random = new();
	private static bool WarningShown = false;
	private static readonly RateLimiter RateLimiter = new(1, TimeSpan.FromSeconds(1), margin: 1);

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		if (!WarningShown)
		{
			WarningShown = true;

			Logger.Warn($"CrushFTP scanning is limited to {RateLimiter.MaxRequestsPerTimeSpan} directories per {RateLimiter.TimeSpan.TotalSeconds:F1} second(s)!");
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
			Logger.Error(ex, $"Error parsing {Parser} for URL: {webDirectory.Url}");
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

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
			Logger.Error(ex, $"Error processing {Parser} for URL: {webDirectory.Url}");
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}
}
