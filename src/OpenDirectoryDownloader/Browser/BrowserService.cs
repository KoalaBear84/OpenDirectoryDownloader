using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Parsing;
using OpenDirectoryDownloader.Protocols.Http;
using OpenDirectoryDownloader.Urls;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using System.Net;

namespace OpenDirectoryDownloader.Browser;

/// <summary>
/// A real (visible) Chrome, for what the crawler can't do itself: let the user solve a Cloudflare challenge, and get
/// the HTML of pages drawn by JavaScript. Started on first use and then kept for the rest of the scan; one page at
/// a time. Cookies the browser gets are copied to the crawler's cookies.
/// </summary>
public sealed class BrowserService(ScanHttp scanHttp, ILogger<BrowserService> logger) : IBrowserRenderer, IAsyncDisposable
{
	private const string CloudflareClearanceKey = "cf_clearance";

	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

	private readonly SemaphoreSlim _lock = new(1, 1);
	private IBrowser _browser;
	private IPage _page;

	/// <summary>The page's HTML once the browser loaded it, or null when it timed out.</summary>
	public async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken);

		try
		{
			IPage page = await GetPageAsync();

			logger.LogWarning("Retrieving HTML through browser for {url}..", url);

			try
			{
				await page.GoToAsync(url, new NavigationOptions
				{
					Timeout = (int)Timeout.TotalMilliseconds,
					WaitUntil = [WaitUntilNavigation.DOMContentLoaded]
				});
			}
			catch (NavigationException ex)
			{
				logger.LogError(ex, "Timeout in navigating to URL {url}", url);
				return null;
			}

			string html = await page.GetContentAsync();

			// So the next requests can hopefully go without the browser
			AddCookies(await page.GetCookiesAsync());

			logger.LogWarning("Retrieved HTML through browser");

			return html;
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// Opens <paramref name="url"/> and waits (at most a minute) for the Cloudflare clearance cookie, while the user
	/// solves the challenge in the browser window.
	/// </summary>
	public async Task<bool> SolveCloudflareAsync(string url, CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken);

		try
		{
			IPage page = await GetPageAsync();

			logger.LogWarning("Cloudflare protection detected, trying to launch browser. Solve protection yourself, indexing will start automatically!");

			try
			{
				await page.GoToAsync(url, new NavigationOptions { Timeout = (int)Timeout.TotalMilliseconds });
			}
			catch (NavigationException)
			{
				// The challenge page can keep navigating, the cookie is what counts
			}

			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(Timeout);

			try
			{
				while (true)
				{
					CookieParam[] cookies = await page.GetCookiesAsync();

					if (cookies.Any(cookie => cookie.Name.StartsWith(CloudflareClearanceKey)))
					{
						AddCookies(cookies);
						logger.LogWarning("Cloudflare OK!");

						return true;
					}

					await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
				}
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				logger.LogError("Looks like Cloudflare protection wasn't solved in time.");

				return false;
			}
		}
		finally
		{
			_lock.Release();
		}
	}

	private void AddCookies(CookieParam[] cookieParams)
	{
		foreach (CookieParam cookieParam in cookieParams)
		{
			try
			{
				scanHttp.Cookies.Add(new Cookie
				{
					Name = cookieParam.Name,
					Domain = cookieParam.Domain,
					Path = cookieParam.Path,
					Expires = UrlHelper.UnixTimestampToDateTime((long)cookieParam.Expires),
					HttpOnly = cookieParam.HttpOnly ?? false,
					Value = cookieParam.Value,
					Secure = cookieParam.Secure ?? false
				});
			}
			catch (CookieException ex)
			{
				logger.LogDebug(ex, "Ignoring browser cookie {name}", cookieParam.Name);
			}
		}
	}

	private async Task<IPage> GetPageAsync()
	{
		if (_page is not null && !_page.IsClosed)
		{
			return _page;
		}

		if (_browser is null || !_browser.IsConnected)
		{
			BrowserFetcher browserFetcher = new();

			if (browserFetcher.GetInstalledBrowsers().All(x => x.BuildId != Chrome.DefaultBuildId))
			{
				logger.LogWarning("Downloading browser... First time it can take a while, depending on your internet connection.");
				InstalledBrowser installedBrowser = await browserFetcher.DownloadAsync(Chrome.DefaultBuildId);
				logger.LogWarning("Downloaded browser. Browser: {browser}, Platform: {platform}, BuildId: {buildId}, ExecutablePath: {executablePath}", installedBrowser.Browser, installedBrowser.Platform, installedBrowser.BuildId, installedBrowser.GetExecutablePath());
			}

			logger.LogWarning("Starting browser..");

			PuppeteerExtra puppeteerExtra = new();

			// Stealth plugin, needed for Cloudflare / hCaptcha
			puppeteerExtra.Use(new StealthPlugin());

			_browser = await puppeteerExtra.LaunchAsync(new LaunchOptions
			{
				// Visible: a Cloudflare challenge has to be solved by the user
				Headless = false,
				Args = ["--no-sandbox", "--disable-setuid-sandbox", $"--user-agent=\"{Constants.UserAgent.Chrome}\""],
				DefaultViewport = null,
				AcceptInsecureCerts = true
			});

			logger.LogInformation("Started browser with PID {processId}", _browser.Process?.Id);
		}

		_page = (await _browser.PagesAsync()).FirstOrDefault() ?? await _browser.NewPageAsync();

		return _page;
	}

	public async ValueTask DisposeAsync()
	{
		if (_browser is not null)
		{
			try
			{
				await _browser.CloseAsync();
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Error closing browser");
			}

			await _browser.DisposeAsync();
		}

		_lock.Dispose();
	}
}
