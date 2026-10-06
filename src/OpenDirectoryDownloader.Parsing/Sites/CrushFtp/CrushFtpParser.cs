using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.Parsing.Sites.CrushFtp;

public static class CrushFtpParser
{
	private const string Parser = "CrushFTP";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory)
	{
		CrushFtpState state = services.Session.SiteState.Get<CrushFtpState>();

		if (!state.WarningShown)
		{
			state.WarningShown = true;

			services.Logger.LogWarning("{parser} scanning is limited to {maxRequestsPerTimeSpan} directories per {seconds:F1} second(s)!", Parser, state.RateLimiter.MaxRequestsPerTimeSpan, state.RateLimiter.TimeSpan.TotalSeconds);
		}

		try
		{
			if (string.IsNullOrWhiteSpace(state.Authentication))
			{
				state.Authentication = services.Cookies?.GetAllCookies().FirstOrDefault(c => c.Name == "currentAuth")?.Value;
				state.FunctionUrl = $"{webDirectory.Uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port, UriFormat.UriEscaped)}/WebInterface/function/";
			}

			webDirectory = await ScanAsync(services, webDirectory);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error parsing {parser} for URL: {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
	{
		services.Logger.LogDebug("Retrieving listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		CrushFtpState state = services.Session.SiteState.Get<CrushFtpState>();

		try
		{
			await state.RateLimiter.RateLimit();

			Dictionary<string, string> postValues = new()
			{
				{ "command", "getXMLListing" },
				{ "format", "JSONOBJ" },
				{ "path", $"{Uri.EscapeDataString(webDirectory.Uri.LocalPath)}%2F" },
				{ "random", Random.Shared.NextDouble().ToString() },
				{ "c2f", state.Authentication }
			};

			HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(state.FunctionUrl, new FormUrlEncodedContent(postValues));

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
			services.Logger.LogError(ex, "Error processing {parser} for URL: {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}
}

/// <summary>The session of the CrushFTP web interface, from its login cookie</summary>
public sealed class CrushFtpState
{
	public string Authentication { get; set; }

	public string FunctionUrl { get; set; }

	public bool WarningShown { get; set; }

	public RateLimiter RateLimiter { get; } = new(1, TimeSpan.FromSeconds(1), margin: 1);
}
