using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using System.Diagnostics;
using System.Net;
using ErrorEventArgs = PuppeteerSharp.ErrorEventArgs;

namespace OpenDirectoryDownloader;

public class BrowserContext : IDisposable
{
	private const string SetCookieHeader = "set-cookie";
	private const string CloudflareClearanceKey = "cf_clearance";

	private IBrowser Browser { get; set; }
	private IPage Page { get; set; }
	private CookieContainer CookieContainer { get; }
	public bool CloudFlare { get; }
	public bool DebugInfo { get; }
	public TimeSpan Timeout { get; set; }
	private CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
	private bool OK { get; set; }
	private static readonly SemaphoreSlim SemaphoreSlimFetcher = new(1, 1);

	private readonly object LockCheckCloudflareCookie = new();

	public BrowserContext(CookieContainer cookieContainer, bool cloudFlare = false, bool debugInfo = false, TimeSpan timeout = default)
	{
		CookieContainer = cookieContainer;
		CloudFlare = cloudFlare;
		DebugInfo = debugInfo;
		Timeout = timeout;
	}

	~BrowserContext()
	{
		Dispose();
	}

	public void Dispose()
	{
		Page?.Dispose();
		Browser?.Dispose();
		CancellationTokenSource.Dispose();
		GC.SuppressFinalize(this);
	}

	private void WriteDebugInfo(string message)
	{
		if (!DebugInfo)
		{
			return;
		}

		Console.WriteLine(message);
	}

	public async Task<bool> DoCloudFlareAsync(string url)
	{
		try
		{
			if (Timeout == default)
			{
				Timeout = TimeSpan.FromMinutes(1);
			}

			CancellationTokenSource.CancelAfter(Timeout);

			await InitializeAsync();

			Stopwatch stopwatch = Stopwatch.StartNew();

			Program.Logger.Debug("Navigating to {url}..", url);

			await Page.GoToAsync(url);

			// Not awaited on purpose, used to run it like a separate thread
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
			Task.Run(() =>
			{
				while (!CancellationTokenSource.IsCancellationRequested)
				{
					CheckCloudflareCookie();

					Task.Delay(TimeSpan.FromSeconds(1)).Wait();
				}
			});
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

			await Task.Delay(TimeSpan.FromSeconds(60), CancellationTokenSource.Token);

			Program.Logger.Debug("Navigation done in {elapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);

			Program.Logger.Debug("Finished with browser!");
		}
		catch (OperationCanceledException ex)
		{
			if (!OK)
			{
				Program.Logger.Error(ex, "Looks like Cloudflare protection wasn't solved in time.");
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error with browser");
		}
		finally
		{
			Program.Logger.Debug("Closing browser");
			await Browser.CloseAsync();
			Program.Logger.Debug("Closed browser");
		}

		return OK;
	}

	public static void AddCookiesToContainer(CookieContainer cookieContainer, CookieParam[] cookieParams)
	{
		foreach (CookieParam cookieParam in cookieParams)
		{
			cookieContainer.Add(new Cookie
			{
				Name = cookieParam.Name,
				Domain = cookieParam.Domain,
				Path = cookieParam.Path,
				Expires = Library.UnixTimestampToDateTime((long)cookieParam.Expires),
				HttpOnly = cookieParam.HttpOnly ?? false,
				Value = cookieParam.Value,
				Secure = cookieParam.Secure ?? false
			});
		}
	}

	public async Task InitializeAsync()
	{
		try
		{
			BrowserFetcher browserFetcher = new();

			await SemaphoreSlimFetcher.WaitAsync();

			try
			{
				if (!browserFetcher.GetInstalledBrowsers().Any(x => x.BuildId == Chrome.DefaultBuildId))
				{
					Program.Logger.Warning("Downloading browser... First time it can take a while, depending on your internet connection.");
					InstalledBrowser installedBrowser = await browserFetcher.DownloadAsync(Chrome.DefaultBuildId);
					Program.Logger.Warning("Downloaded browser. Browser: {browser}, Platform: {platform}, BuildId: {buildId}, ExecutablePath: {executablePath}", installedBrowser.Browser, installedBrowser.Platform, installedBrowser.BuildId, installedBrowser.GetExecutablePath());
				}
			}
			finally
			{
				SemaphoreSlimFetcher.Release();
			}

			Program.Logger.Debug("Creating browser...");

			PuppeteerExtra puppeteerExtra = new();

			// Use stealth plugin (needed for Cloudflare / hCaptcha)
			puppeteerExtra.Use(new StealthPlugin());

			Browser = await puppeteerExtra.LaunchAsync(new LaunchOptions
			{
				Headless = false,
				Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", $"--user-agent=\"{Constants.UserAgent.Chrome}\"" },
				DefaultViewport = null,
				IgnoreHTTPSErrors = true
			});

			Program.Logger.Information("Started browser with PID {processId}", Browser.Process.Id);

			Browser.Closed += Browser_Closed;
			Browser.Disconnected += Browser_Disconnected;
			Browser.TargetChanged += Browser_TargetChanged;
			Browser.TargetCreated += Browser_TargetCreated;
			Browser.TargetDestroyed += Browser_TargetDestroyed;

			Program.Logger.Debug("Created browser.");

			Program.Logger.Debug("Creating page...");

			Page = (await Browser.PagesAsync())[0];

			Page.Close += Page_Close;
			Page.Console += Page_Console;
			Page.Dialog += Page_Dialog;
			Page.DOMContentLoaded += Page_DOMContentLoaded;
			Page.Error += Page_Error;
			Page.FrameAttached += Page_FrameAttached;
			Page.FrameDetached += Page_FrameDetached;
			Page.FrameNavigated += Page_FrameNavigated;
			Page.Load += Page_Load;
			Page.Metrics += Page_Metrics;
			Page.PageError += Page_PageError;
			Page.Popup += Page_Popup;
			Page.Request += Page_Request;
			Page.RequestFailed += Page_RequestFailed;
			Page.RequestFinished += Page_RequestFinished;
			Page.RequestServedFromCache += Page_RequestServedFromCache;
			Page.Response += Page_Response;
			Page.WorkerCreated += Page_WorkerCreated;
			Page.WorkerDestroyed += Page_WorkerDestroyed;

			Program.Logger.Debug("Created page.");
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error with initializing browser");
			throw;
		}
	}

	public async Task<CookieParam[]> GetCookiesAsync()
	{
		return await Page.GetCookiesAsync();
	}

	public async Task<string> GetHtml(string url)
	{
		try
		{
			if (Timeout == default)
			{
				Timeout = TimeSpan.FromMinutes(1);
			}

			CancellationTokenSource.CancelAfter(Timeout);

			Stopwatch stopwatch = Stopwatch.StartNew();

			Program.Logger.Debug("Navigating to {url}..", url);

			NavigationOptions navigationOptions = new()
			{
				Timeout = (int)Timeout.TotalMilliseconds,
				WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
			};

			await Page.GoToAsync(url, navigationOptions);
			Program.Logger.Debug("Navigation done in {elapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);

			string html = await Page.GetContentAsync();

			return html;
		}
		catch (OperationCanceledException ex)
		{
			if (!OK)
			{
				Program.Logger.Error(ex, "Timeout in navigating to URL");
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error with browser");
			throw;
		}

		return null;
	}

	private void Browser_Closed(object sender, EventArgs e)
	{
		WriteDebugInfo("Browser_Closed");
	}

	private void Browser_Disconnected(object sender, EventArgs e)
	{
		WriteDebugInfo("Browser_Disconnected");
	}

	private void Browser_TargetChanged(object sender, TargetChangedArgs e)
	{
		WriteDebugInfo($"Browser_TargetChanged: {e.Target.Url}");
	}

	private void Browser_TargetCreated(object sender, TargetChangedArgs e)
	{
		WriteDebugInfo($"Browser_TargetCreated: {e.Target.Url}");
	}

	private void Browser_TargetDestroyed(object sender, TargetChangedArgs e)
	{
		WriteDebugInfo($"Browser_TargetDestroyed: {e.Target.Url}");
	}

	private void Page_Close(object sender, EventArgs e)
	{
		WriteDebugInfo($"Page_Close");
	}

	private void Page_Console(object sender, ConsoleEventArgs e)
	{
		WriteDebugInfo($"Page_Console: {e.Message.Type}, {e.Message.Text}");
	}

	private void Page_Dialog(object sender, DialogEventArgs e)
	{
		WriteDebugInfo($"Page_Dialog: {e.Dialog.DialogType}, {e.Dialog.Message}");
	}

	private void Page_DOMContentLoaded(object sender, EventArgs e)
	{
		WriteDebugInfo("Page_DOMContentLoaded");
	}

	private void Page_Error(object sender, ErrorEventArgs e)
	{
		WriteDebugInfo($"Page_Error: {e.Error}");
	}

	private void Page_FrameAttached(object sender, FrameEventArgs e)
	{
		WriteDebugInfo($"Page_FrameAttached: {e.Frame.Name}, {e.Frame.Url}");
	}

	private void Page_FrameDetached(object sender, FrameEventArgs e)
	{
		WriteDebugInfo($"Page_FrameDetached: {e.Frame.Name}, {e.Frame.Url}");
	}

	private void Page_FrameNavigated(object sender, FrameEventArgs e)
	{
		WriteDebugInfo($"Page_FrameNavigated: {e.Frame.Name}, {e.Frame.Url}");
	}

	private void Page_Load(object sender, EventArgs e)
	{
		WriteDebugInfo("Page_Load");
	}

	private void Page_Metrics(object sender, MetricEventArgs e)
	{
		WriteDebugInfo($"Page_Metrics: {e.Title}, {e.Metrics.Count}");
	}

	private void Page_PageError(object sender, PageErrorEventArgs e)
	{
		WriteDebugInfo($"Page_PageError: {e.Message}");
	}

	private void Page_Popup(object sender, PopupEventArgs e)
	{
		WriteDebugInfo($"Page_Popup: {e.PopupPage.Url}");
	}

	private void Page_Request(object sender, RequestEventArgs e)
	{
		WriteDebugInfo($"Page_Request: [{e.Request?.Method}] {e.Request?.Url}");
	}

	private void Page_RequestFailed(object sender, RequestEventArgs e)
	{
		WriteDebugInfo($"Page_RequestFailed: [{e.Request?.Method}] {e.Request?.Url}");
	}

	private void Page_RequestFinished(object sender, RequestEventArgs e)
	{
		WriteDebugInfo($"Page_RequestFinished: [{e.Request?.Method}] {e.Request?.Url}");
	}

	private void Page_RequestServedFromCache(object sender, RequestEventArgs e)
	{
		WriteDebugInfo($"Page_RequestServedFromCache: [{e.Request?.Method}] {e.Request?.Url}");
	}

	private void Page_Response(object sender, ResponseCreatedEventArgs e)
	{
		WriteDebugInfo($"Page_Response: {e.Response.Url}.{Environment.NewLine}Headers: {string.Join(Environment.NewLine, e.Response.Headers.Select(h => $"{h.Key}: {h.Value}"))}");

		if (e.Response.Headers.TryGetValue(SetCookieHeader, out string cookieHeader))
		{
			Uri uri = new(e.Response.Url);
			string baseUrl = $"{uri.Scheme}://{uri.Host}";

			WriteDebugInfo($"Cookies: {cookieHeader}");

			string theCookie = cookieHeader.Split('\n').FirstOrDefault(cookie => cookie.StartsWith(CloudflareClearanceKey));

			if (theCookie != null)
			{
				CookieContainer.SetCookies(new Uri(baseUrl), theCookie);

				if (CloudFlare)
				{
					Cookie cloudflareClearance = CookieContainer.GetCookies(new Uri(baseUrl)).FirstOrDefault(c => c.Name == CloudflareClearanceKey);

					if (cloudflareClearance != null)
					{
						WriteDebugInfo($"Cloudflare clearance cookie found: {cloudflareClearance.Value}");

						OK = true;
						CancellationTokenSource.Cancel();
					}
				}
			}
		}
		else
		{
			CheckCloudflareCookie();
		}
	}

	private void CheckCloudflareCookie()
	{
		if (OK)
		{
			return;
		}

		if (CloudFlare)
		{
			Task.Run(() =>
			{
				lock (LockCheckCloudflareCookie)
				{
					WriteDebugInfo("Retrieve cookies..");

					try
					{
						CookieParam[] cookieParams = Page.GetCookiesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
						WriteDebugInfo($"Retrieved {cookieParams.Length} cookies");

						CookieParam cloudflareClearanceCookie = cookieParams.FirstOrDefault(cookie => cookie.Name.StartsWith(CloudflareClearanceKey));

						if (cloudflareClearanceCookie is not null)
						{
							WriteDebugInfo($"Cloudflare clearance cookie found: {cloudflareClearanceCookie.Value}");

							AddCookiesToContainer(CookieContainer, cookieParams);

							OK = true;
							CancellationTokenSource.Cancel();
						}
					}
					catch
					{
						// No logging
					}
				}
			});
		}
	}

	private void Page_WorkerCreated(object sender, WorkerEventArgs e)
	{
		WriteDebugInfo($"Page_WorkerCreated: {e.Worker.Url}");
	}

	private void Page_WorkerDestroyed(object sender, WorkerEventArgs e)
	{
		WriteDebugInfo($"Page_WorkerDestroyed: {e.Worker.Url}");
	}
}
