using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Browser;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Crawling;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing;
using OpenDirectoryDownloader.Parsing.Html;
using OpenDirectoryDownloader.Parsing.Listings;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Parsing.Sites.AList;
using OpenDirectoryDownloader.Parsing.Sites.AmazonS3;
using OpenDirectoryDownloader.Parsing.Sites.Calibre;
using OpenDirectoryDownloader.Parsing.Sites.CrushFtp;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex.GdIndex;
using OpenDirectoryDownloader.Parsing.Sites.GitHub;
using OpenDirectoryDownloader.Parsing.Sites.HFS;
using OpenDirectoryDownloader.Scanning;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>
/// Gets one directory over HTTP and hands the result to the crawl engine. THE ORDER OF THE STEPS MATTERS: sites
/// recognized before any request, then by the response's headers, then the workarounds for the first request,
/// and only then the page is parsed. Each step either finishes the directory or lets the next one look.
/// </summary>
public sealed partial class HttpDirectoryFetcher(
	ScanSession session,
	ScanHttp scanHttp,
	ParserServices parserServices,
	BrowserService browser,
	CrawlOptions crawlOptions,
	Credentials credentials,
	ILogger<HttpDirectoryFetcher> logger)
{
	/// <summary>Bigger than this is a file that happens to be served without a filename, not a listing</summary>
	private const long MaxListingLength = 20 * Constants.Megabyte;

	private HttpClient Client => scanHttp.Client;

	/// <param name="processed">Called with the parsed directory, and whether its subdirectories should be crawled</param>
	public async Task FetchAsync(DirectoryWork work, Func<WebDirectory, bool, Task> processed, CancellationToken cancellationToken)
	{
		WebDirectory webDirectory = work.Directory;

		// --- Sites that are read through their API, not their pages

		if (session.SiteState.Find<GoogleDriveIndexState>()?.GdIndexRootId is not null)
		{
			await processed(await GdIndexParser.ParseIndex(parserServices, Detached(webDirectory), string.Empty), true);
			return;
		}

		if (webDirectory.Parser == HfsParser.Parser)
		{
			await processed(await HfsParser.ScanAsync(parserServices, webDirectory, null), true);
			return;
		}

		if (webDirectory.Uri.Host is Constants.GitHubDomain or Constants.GitHubApiDomain)
		{
			await processed(await GitHubParser.ParseIndex(parserServices, webDirectory, credentials.GitHubToken), false);
			return;
		}

		if (webDirectory.Parser == AListParser.Parser)
		{
			await processed(await AListParser.ParseIndex(parserServices, webDirectory), true);
			return;
		}

		// --- The request, and what its headers tell

		HttpResponseMessage response = null;

		try
		{
			try
			{
				response = await Client.GetAsync(webDirectory.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (TaskCanceledException)
			{
				// Timeout: retry
				throw;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
			}

			string server = response?.Headers.Server.FirstOrDefault()?.Product?.Name.ToLowerInvariant();

			if (server == "amazons3")
			{
				await processed(await AmazonS3Parser.ParseIndex(parserServices, webDirectory, hasHeader: true), true);
				return;
			}

			if (response?.Content.Headers.ContentType?.MediaType == "application/xml")
			{
				string xml = await BodyReader.ReadTextAsync(response, cancellationToken);

				if (xml.Contains("ListBucketResult"))
				{
					await processed(await AmazonS3Parser.ParseIndex(parserServices, webDirectory, hasHeader: false), true);
					return;
				}
			}

			if (server == "crushftp")
			{
				await processed(await CrushFtpParser.ParseIndex(parserServices, webDirectory), true);
				return;
			}

			if (response?.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden && server == "cloudflare")
			{
				string cloudflareHtml = await BodyReader.ReadTextAsync(response, cancellationToken);

				if (CloudflareChallengeFormRegex().IsMatch(cloudflareHtml))
				{
					if (scanHttp.State.Profile.UserAgent is null)
					{
						scanHttp.State.Update(profile => profile with { UserAgent = Constants.UserAgent.Chrome });
						response = await RefetchAsync(response, webDirectory.Url, cancellationToken);
					}

					if (crawlOptions.NoBrowser)
					{
						logger.LogError("Cloudflare protection detected, --no-browser option active, cannot continue!");
						return;
					}

					if (await browser.SolveCloudflareAsync(session.Root.Url, cancellationToken))
					{
						logger.LogWarning("User agent forced to Chrome because of Cloudflare");
						scanHttp.State.Update(profile => profile with { UserAgent = Constants.UserAgent.Chrome });
					}
					else
					{
						logger.LogError("Cloudflare failed!");
					}

					response = await RefetchAsync(response, webDirectory.Url, cancellationToken);
				}
			}

			if (response?.StatusCode == HttpStatusCode.MovedPermanently && response.Headers.Location != null)
			{
				response = await RefetchAsync(response, response.Headers.Location.ToString(), cancellationToken);
			}

			if (response?.Content?.Headers.ContentLength > MaxListingLength)
			{
				work.ConvertToFile(response.Content.Headers.ContentLength);
				return;
			}

			string html = null;

			if (server == "diamondcdn" && (int?)response?.StatusCode == 418 && !crawlOptions.NoBrowser)
			{
				html = await browser.GetHtmlAsync(webDirectory.Url, cancellationToken);

				if (html is not null)
				{
					// The browser got it
					response.StatusCode = HttpStatusCode.OK;
				}
			}

			// --- The body

			if (response?.IsSuccessStatusCode == true)
			{
				SetRootUrl(response);

				if (html is null)
				{
					html = await BodyReader.ReadHtmlAsync(response, cancellationToken);

					if (html is null)
					{
						// CrushFTP when rate limiting / blocking
						if (response.ReasonPhrase == "BANNED")
						{
							return;
						}

						work.ConvertToFile(response.Content.Headers.ContentLength);
						return;
					}
				}

				if (html.Contains("document.cookie"))
				{
					Match cookieRegexMatch = CookieRegex().Match(html);

					if (cookieRegexMatch.Success)
					{
						scanHttp.Cookies.SetCookies(webDirectory.Uri, cookieRegexMatch.Groups["Cookie"].Value);

						// Again, with the cookie
						response = await RefetchAsync(response, webDirectory.Url, cancellationToken);

						string htmlText = await BodyReader.ReadHtmlAsync(response, cancellationToken);

						if (htmlText != null)
						{
							html = htmlText;
						}
					}
				}
			}

			// --- The first request gets some fallbacks; some servers only answer a known client

			if ((scanHttp.State.FirstRequest && string.IsNullOrWhiteSpace(html)) || html?.Contains("HTTP_USER_AGENT") == true)
			{
				logger.LogWarning("First request fails, using Curl fallback User-Agent");
				(response, html, bool converted) = await TryUserAgentAsync(response, html, work, profile => profile with { UserAgent = Constants.UserAgent.Curl }, "Curl", cancellationToken);

				if (converted)
				{
					return;
				}
			}

			if (scanHttp.State.FirstRequest && string.IsNullOrWhiteSpace(html))
			{
				logger.LogWarning("First request fails, using Chrome fallback User-Agent");
				(response, html, bool converted) = await TryUserAgentAsync(response, html, work, profile => profile with { UserAgent = Constants.UserAgent.Chrome }, "Chrome", cancellationToken);

				if (converted)
				{
					return;
				}
			}

			if (scanHttp.State.FirstRequest && string.IsNullOrWhiteSpace(html))
			{
				logger.LogWarning("First request fails, using Chrome fallback User-Agent (with extra headers)");
				(response, html, bool converted) = await TryUserAgentAsync(response, html, work, profile => profile with { UserAgent = Constants.UserAgent.Chrome, ExtraBrowserAcceptHeaders = true }, "Chrome (with extra headers)", cancellationToken);

				if (converted)
				{
					return;
				}
			}

			if (response is null)
			{
				throw new Exception($"Error retrieving directory listing (empty/null) for {webDirectory.Url}");
			}

			if (scanHttp.State.Profile.Referer is null)
			{
				string referer = webDirectory.Url;
				scanHttp.State.Update(profile => profile.Referer is null ? profile with { Referer = referer } : profile);
			}

			// --- Calibre has its own API

			bool calibreDetected = false;
			string calibreVersionString = string.Empty;

			if (response.IsSuccessStatusCode)
			{
				scanHttp.State.FirstRequest = false;

				List<string> serverHeaders = response.Headers.TryGetValues("Server", out IEnumerable<string> serverValues) ? [.. serverValues] : [];
				calibreDetected = serverHeaders.Any(h => h.Contains("calibre"));

				if (calibreDetected)
				{
					calibreVersionString = string.Join("/", serverHeaders);
				}
				else
				{
					if (html == null)
					{
						html = await BodyReader.ReadHtmlAsync(response, cancellationToken);

						if (html == null)
						{
							work.ConvertToFile(response.Content.Headers.ContentLength);
							return;
						}
					}

					const string calibreVersionIdentifier = "CALIBRE_VERSION = \"";
					calibreDetected = html.Contains(calibreVersionIdentifier);

					if (calibreDetected)
					{
						// The version between the quotes
						int versionStart = html.IndexOf(calibreVersionIdentifier, StringComparison.Ordinal) + calibreVersionIdentifier.Length;
						int versionEnd = html.IndexOf('"', versionStart);
						calibreVersionString = versionEnd > versionStart ? html[versionStart..versionEnd] : string.Empty;
					}
				}
			}

			if (calibreDetected)
			{
				Version calibreVersion = CalibreParser.ParseVersion(calibreVersionString);

				parserServices.Console.WriteLine($"Calibre {calibreVersion} detected! I will index it at max 100 books per 30 seconds, else it will break Calibre...");
				logger.LogInformation("Calibre {calibreVersion} detected! I will index it at max 100 books per 30 seconds, else it will break Calibre...", calibreVersion);

				WebDirectory calibreDirectory = Detached(webDirectory);
				await CalibreParser.ParseCalibre(parserServices, response.RequestMessage.RequestUri, calibreDirectory, calibreVersion, cancellationToken);
				await processed(calibreDirectory, false);

				return;
			}

			// --- Redirected?

			string requestUrl = response.RequestMessage.RequestUri.ToString();

			if (response.IsSuccessStatusCode && webDirectory.Url != requestUrl)
			{
				// Soft rate limit
				if (requestUrl.EndsWith("overload.html"))
				{
					throw new SoftRateLimitException();
				}

				webDirectory.Url = requestUrl;
			}

			logger.LogDebug("[{thread}] Finish download [HTTP {httpcode}] '{url}', size: {length}", work.WorkerName, response.StatusCode, webDirectory.Url, FileSizeHelper.ToHumanReadable(html?.Length));

			// --- Parse, only when it is still the same site

			string requestHost = response.RequestMessage.RequestUri.Host;

			if (requestHost != session.Root.Uri.Host && requestHost != Constants.PCloudDomain1 && requestHost != Constants.PCloudDomain2)
			{
				logger.LogWarning("[{thread}] Skipped result of '{url}' which points to '{redirectedUrl}'", work.WorkerName, webDirectory.Url, response.RequestMessage.RequestUri);
				session.Counters.AddSkipped();
				return;
			}

			session.HttpStatusCodes.Add((int)response.StatusCode);

			if (!response.IsSuccessStatusCode)
			{
				await RetryAfter.WaitIfAskedAsync(logger, work.WorkerName, webDirectory, response, work.ExtendTimeout, cancellationToken);

				response.EnsureSuccessStatusCode();
			}

			html ??= await BodyReader.ReadTextAsync(response, cancellationToken);

			if (html.Length > Constants.Megabyte)
			{
				logger.LogWarning("Large response of {length} for {url}", FileSizeHelper.ToHumanReadable(html.Length), webDirectory.Url);
			}

			session.Counters.AddHttpTraffic(html.Length);

			WebDirectory parsedWebDirectory = await HtmlListingParser.ParseAsync(parserServices, webDirectory, html, response);

			// That listing lists its whole tree at once
			bool processSubdirectories = parsedWebDirectory.Parser != DirectoryListingModel01Parser.Parser;

			await processed(parsedWebDirectory, processSubdirectories);
		}
		finally
		{
			response?.Dispose();
		}
	}

	/// <summary>A new request to <paramref name="url"/> replacing <paramref name="previous"/> (which is disposed)</summary>
	private async Task<HttpResponseMessage> RefetchAsync(HttpResponseMessage previous, string url, CancellationToken cancellationToken)
	{
		previous?.Dispose();

		return await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	/// <summary>Tries the directory again with another user agent; when that works, the whole scan keeps it.</summary>
	private async Task<(HttpResponseMessage Response, string Html, bool Converted)> TryUserAgentAsync(HttpResponseMessage response, string html, DirectoryWork work, Func<RequestProfile, RequestProfile> change, string name, CancellationToken cancellationToken)
	{
		scanHttp.State.Update(change);
		response = await RefetchAsync(response, work.Directory.Url, cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			return (response, html, false);
		}

		logger.LogWarning("Yes, {userAgent} User-Agent did the trick!", name);

		SetRootUrl(response);

		string htmlText = await BodyReader.ReadHtmlAsync(response, cancellationToken);

		if (htmlText is null)
		{
			work.ConvertToFile(response.Content.Headers.ContentLength);

			return (response, html, true);
		}

		return (response, htmlText, false);
	}

	/// <summary>The first request may have been redirected: then that is the real root URL.</summary>
	private void SetRootUrl(HttpResponseMessage response)
	{
		if (!scanHttp.State.FirstRequest)
		{
			return;
		}

		string requestUrl = response.RequestMessage?.RequestUri?.ToString();

		if (session.Root.Url == requestUrl)
		{
			return;
		}

		if (session.Root.Uri.Host != response.RequestMessage?.RequestUri?.Host)
		{
			logger.LogError("Response is NOT from requested host '{badHost}', but from {goodHost}, maybe retry with different user agent, see Command Line options", session.Root.Uri.Host, response.RequestMessage.RequestUri.Host);
		}

		session.Root.Url = requestUrl;
		logger.LogWarning("Retrieved URL: {url}", session.Root.Url);
	}

	/// <summary>A copy of the directory to give a parser that fills the directory it is given, so the result can be added like any other</summary>
	private static WebDirectory Detached(WebDirectory webDirectory)
	{
		return new WebDirectory(webDirectory.ParentDirectory)
		{
			Url = webDirectory.Url,
			Name = webDirectory.Name,
			Parser = webDirectory.Parser
		};
	}

	[GeneratedRegex("document\\.cookie\\s?=\\s?\"(?<Cookie>.*?)\"")]
	private static partial Regex CookieRegex();

	[GeneratedRegex(@"<form (?:class|id)=""challenge-form[^>]*>([\s\S]*?)<\/form>")]
	private static partial Regex CloudflareChallengeFormRegex();
}
