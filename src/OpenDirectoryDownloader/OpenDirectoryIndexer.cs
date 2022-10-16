using Newtonsoft.Json;
using OpenDirectoryDownloader.Calibre;
using OpenDirectoryDownloader.FileUpload;
using OpenDirectoryDownloader.GoogleDrive;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.AmazonS3;
using OpenDirectoryDownloader.Site.CrushFtp;
using OpenDirectoryDownloader.Site.GitHub;
using Polly;
using Polly.Retry;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using TextCopy;

namespace OpenDirectoryDownloader;

public class OpenDirectoryIndexer
{
	public static Session Session { get; set; }
	public static bool ShowStatistics { get; set; } = true;

	public OpenDirectoryIndexerSettings OpenDirectoryIndexerSettings { get; set; }

	public ConcurrentQueue<WebDirectory> WebDirectoriesQueue { get; set; } = new ConcurrentQueue<WebDirectory>();
	public int RunningWebDirectoryThreads;
	public Task[] WebDirectoryProcessors;
	public Dictionary<string, WebDirectory> WebDirectoryProcessorInfo = new();
	public readonly object WebDirectoryProcessorInfoLock = new();

	public ConcurrentQueue<WebFile> WebFilesFileSizeQueue { get; set; } = new ConcurrentQueue<WebFile>();
	public int RunningWebFileFileSizeThreads;
	public Task[] WebFileFileSizeProcessors;

	public CancellationTokenSource IndexingTaskCTS { get; set; }
	public Task IndexingTask { get; set; }

	private bool FirstRequest { get; set; } = true;
	private static bool RateLimited { get; set; }

	private SocketsHttpHandler SocketsHttpHandler { get; set; }
	private HttpClient HttpClient { get; set; }
	public static BrowserContext BrowserContext { get; set; }
	public static CookieContainer CookieContainer { get; set; } = new();

	private System.Timers.Timer TimerStatistics { get; set; }

	private static readonly Random Jitterer = new();

	private static readonly List<string> KnownErrorPaths = new()
	{
		"cgi-bin/",
		"lost%2Bfound/"
	};

	private GoogleDriveIndexer GoogleDriveIndexer { get; set; }

	private readonly AsyncRetryPolicy RetryPolicy = Policy
		.Handle<Exception>()
		.WaitAndRetryAsync(100,
			sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, retryAttempt))) + TimeSpan.FromMilliseconds(Jitterer.Next(0, 200)),
			onRetry: (ex, span, retryCount, context) =>
			{
				WebDirectory webDirectory = context["WebDirectory"] as WebDirectory;
				string threadName = (string)context["Thread"];
				double waitTime = span.TotalSeconds;

				string relativeUrl = webDirectory.Uri.PathAndQuery;

				if (ex is CancelException)
				{
					Program.Logger.Warning("[{thread}] Cancelling: {error}", threadName, ex.Message);
					(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
				}
				else if (ex is SilentException)
				{
					// Silence
				}
				else if (ex is SoftRateLimitException)
				{
					Program.Logger.Warning("[{thread}] Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, retryCount, relativeUrl, waitTime);
				}
				else if (retryCount <= 4 && ex is TaskCanceledException taskCanceledException && taskCanceledException.InnerException is TimeoutException timeoutException)
				{
					Program.Logger.Warning("[{thread}] Timeout (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, retryCount, relativeUrl, waitTime);
				}
				else if (ex is HttpRequestException httpRequestException)
				{
					if (OperatingSystem.IsWindows() && ex.InnerException is AuthenticationException)
					{
						Program.Logger.Warning("[{thread}] Please check readme to fix possible TLS 1.3 issue: https://github.com/KoalaBear84/OpenDirectoryDownloader/#tls-errors-windows", threadName);
					}

					int httpStatusCode = (int)(httpRequestException.StatusCode ?? 0);

					if (KnownErrorPaths.Contains(webDirectory.Uri.Segments.LastOrDefault()))
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Cancelling known error on try {retryCount} for url '{relativeUrl}'.", threadName, httpStatusCode, retryCount, relativeUrl);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if (httpRequestException.StatusCode == HttpStatusCode.ServiceUnavailable || httpRequestException.StatusCode == HttpStatusCode.TooManyRequests)
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, retryCount, relativeUrl, waitTime);
						RateLimited = true;
					}
					else if (httpRequestException.StatusCode == HttpStatusCode.LoopDetected)
					{
						// It could be that a Retry-After header is returned, which should be the seconds of time to wait, but this could be as high as 14.400 which is 4 hours!
						// But a request will probably be successful after just a couple of seconds
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Rate limited / out of capacity (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, retryCount, relativeUrl, waitTime);
						RateLimited = true;
					}
					else if (ex.Message.Contains("No connection could be made because the target machine actively refused it."))
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Rate limited? (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, retryCount, relativeUrl, waitTime);
						RateLimited = true;
					}
					else if (!Session.GDIndex && (httpRequestException.StatusCode == HttpStatusCode.NotFound || ex.Message == "No such host is known."))
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Error '{error}' (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, ex.Message, retryCount, relativeUrl, waitTime);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if (httpRequestException.StatusCode is null && ex.InnerException?.Message == "The requested name is valid, but no data of the requested type was found.")
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Domain does not exist? Possible DNS issue. Skipping..", threadName, httpStatusCode);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if ((httpRequestException.StatusCode == HttpStatusCode.Forbidden || httpRequestException.StatusCode == HttpStatusCode.Unauthorized) && retryCount >= 3)
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Error '{error}' retrieving on try {retryCount}) for '{relativeUrl}'. Skipping..", threadName, httpStatusCode, ex.Message, retryCount, relativeUrl);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if (retryCount <= 4)
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Error '{error}' retrieving on try {retryCount}) for '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, GetExceptionWithInner(ex), retryCount, relativeUrl, waitTime);
					}
					else
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Cancelling on try {retryCount} for url '{relativeUrl}'.", threadName, httpStatusCode, retryCount, relativeUrl);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
				}
				else
				{
					if (retryCount <= 4)
					{
						Program.Logger.Warning("[{thread}] Error '{error}' retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, GetExceptionWithInner(ex), retryCount, relativeUrl, waitTime);
					}
					else
					{
						Program.Logger.Warning("[{thread}] Cancelling on try {retryCount} for url '{relativeUrl}'.", threadName, retryCount, relativeUrl);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
				}
			}
		);

	private static string GetExceptionWithInner(Exception ex)
	{
		string errorMessage = ex.Message;
		Exception exInner = ex;

		while (exInner.InnerException != null)
		{
			errorMessage += $"{Environment.NewLine} -> {exInner.InnerException.Message}";
			exInner = exInner.InnerException;
		}

		return errorMessage;
	}

	public OpenDirectoryIndexer(OpenDirectoryIndexerSettings openDirectoryIndexerSettings)
	{
		OpenDirectoryIndexerSettings = openDirectoryIndexerSettings;

		SocketsHttpHandler = new SocketsHttpHandler()
		{
			SslOptions = new SslClientAuthenticationOptions
			{
				RemoteCertificateValidationCallback = delegate { return true; }
			},
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
			CookieContainer = CookieContainer
		};

		if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyAddress))
		{
			WebProxy webProxy = new()
			{
				Address = new Uri(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyAddress),
			};

			if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyUsername) || !string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyPassword))
			{
				webProxy.Credentials = new NetworkCredential(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyUsername, OpenDirectoryIndexerSettings.CommandLineOptions.ProxyPassword);
			}

			SocketsHttpHandler.Proxy = webProxy;
		}

		// Debugging
		//LoggingHandler loggingHandler = new(SocketsHttpHandler);
		//HttpClient = new HttpClient(loggingHandler)

		HttpClient = new HttpClient(SocketsHttpHandler)
		{
			Timeout = TimeSpan.FromSeconds(OpenDirectoryIndexerSettings.Timeout)
		};

		HttpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
		HttpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
		HttpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,nl;q=0.8");

		foreach (string customHeader in OpenDirectoryIndexerSettings.CommandLineOptions.Header)
		{
			if (!customHeader.Contains(':'))
			{
				Program.Logger.Warning("Invalid header specified: '{customHeader}' should contain the header name and value, separated by a colon (:). Header will be ignored.", customHeader);
				continue;
			}

			string[] splitHeader = customHeader.Split(':');

			string headerName = splitHeader[0].TrimStart();
			string headerValue = string.Join(":", splitHeader.Skip(1)).TrimStart();

			if (headerName.ToLowerInvariant() == "cookie")
			{
				string[] cookies = headerValue.Split(';').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();

				foreach (string cookie in cookies)
				{
					string[] splitCookie = cookie.Split('=');

					if (splitCookie.Length != 2)
					{
						Program.Logger.Warning("Invalid cookie found: '{cookie}' should contain a cookie name and value, separated by '='. Cookie will be ignored.", cookie);
						continue;
					}

					Program.Logger.Warning("Adding cookie: name={name}, value={value}", splitCookie[0], splitCookie[1]);
					CookieContainer.Add(new Uri(OpenDirectoryIndexerSettings.Url), new Cookie(splitCookie[0], splitCookie[1]));
				}
			}
			else
			{
				HttpClient.DefaultRequestHeaders.Add(headerName, headerValue);
			}
		}

		if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.Username) || !string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.Password))
		{
			HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{OpenDirectoryIndexerSettings.Username}:{OpenDirectoryIndexerSettings.Password}")));
		}

		// Fix encoding issue with "windows-1251"
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

		WebDirectoryProcessors = new Task[OpenDirectoryIndexerSettings.Threads];
		WebFileFileSizeProcessors = new Task[OpenDirectoryIndexerSettings.Threads];

		//HttpClient.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent.Curl);
		//HttpClient.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent.Chrome);
	}

	public async void StartIndexingAsync()
	{
		bool fromFile = !string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.FileName);

		if (fromFile)
		{
			Session = Library.LoadSessionJson(OpenDirectoryIndexerSettings.FileName);
			Console.WriteLine(Statistics.GetSessionStats(Session, includeExtensions: true));
			Console.ReadKey(intercept: true);
			return;
		}
		else
		{
			Session = new Session
			{
				Started = DateTimeOffset.UtcNow,
				Root = new WebDirectory(parentWebDirectory: null)
				{
					Name = Constants.Root,
					Url = OpenDirectoryIndexerSettings.Url
				},
				MaxThreads = OpenDirectoryIndexerSettings.Threads,
				CommandLineOptions = OpenDirectoryIndexerSettings.CommandLineOptions
			};
		}

		Session.MaxThreads = OpenDirectoryIndexerSettings.Threads;

		if (Session.Root.Uri.Host == Constants.GoogleDriveDomain)
		{
			Program.Logger.Warning("{indexer} scanning is limited to {directoriesPerSecond} directories per second!", "Google Drive", 9);
		}

		if (Session.Root.Uri.Host == Constants.GitHubDomain)
		{
			Program.Logger.Warning("{indexer} scanning has a very low rate limiting of {requestsPerHour} directories/requests per hour!", "GitHub", 60);

			if (Session.MaxThreads != 1)
			{
				Session.MaxThreads = 1;
				Program.Logger.Warning("Reduce threads to {threadCount} because of {indexert}", 1, "GitHub");
			}
		}

		if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
		{
			Program.Logger.Warning("Retrieving FTP(S) software!");

			if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
			{
				if (Session.Root.Uri.Port == -1)
				{
					Program.Logger.Warning("Using default port (990) for FTPS");

					UriBuilder uriBuilder = new(Session.Root.Uri)
					{
						Port = 990
					};

					Session.Root.Url = uriBuilder.Uri.ToString();
				}
			}

			string serverInfo = await FtpParser.GetFtpServerInfo(Session.Root, OpenDirectoryIndexerSettings.Username, OpenDirectoryIndexerSettings.Password);

			if (string.IsNullOrWhiteSpace(serverInfo))
			{
				serverInfo = "Failed or no server info available.";
			}
			else
			{
				// Remove IP from server info
				Regex.Replace(serverInfo, @"(Connected to )(\d*\.\d*.\d*.\d*)", "$1IP Address");

				Session.Description = $"FTP INFO{Environment.NewLine}{serverInfo}";
			}

			Program.Logger.Warning(serverInfo);
		}

		TimerStatistics = new System.Timers.Timer
		{
			Enabled = true,
			Interval = TimeSpan.FromSeconds(30).TotalMilliseconds
		};

		TimerStatistics.Elapsed += TimerStatistics_Elapsed;

		IndexingTask = Task.Run(async () =>
		{
			try
			{
				WebDirectoriesQueue = new ConcurrentQueue<WebDirectory>();

				if (fromFile)
				{
					SetParentDirectories(Session.Root);

					// TODO: Add unfinished items to queue, very complicated, we need to ALSO fill the ParentDirectory...
					//// With filter predicate, with selection function
					//var flatList = nodes.Flatten(n => n.IsDeleted == false, n => n.Children);
					//var directoriesToDo = Session.Root.Subdirectories.Flatten(null, wd => wd.Subdirectories).Where(wd => !wd.Finished);
				}
				else
				{
					// Add root
					WebDirectoriesQueue.Enqueue(Session.Root);
				}

				IndexingTaskCTS = new CancellationTokenSource();

				for (int i = 1; i <= WebDirectoryProcessors.Length; i++)
				{
					string processorId = i.ToString().PadLeft(WebDirectoryProcessors.Length.ToString().Length, '0');

					WebDirectoryProcessors[i - 1] = WebDirectoryProcessor(WebDirectoriesQueue, $"P{processorId}", IndexingTaskCTS.Token);
				}

				for (int i = 1; i <= WebFileFileSizeProcessors.Length; i++)
				{
					string processorId = i.ToString().PadLeft(WebFileFileSizeProcessors.Length.ToString().Length, '0');

					WebFileFileSizeProcessors[i - 1] = WebFileFileSizeProcessor(WebFilesFileSizeQueue, $"P{processorId}", WebDirectoryProcessors, IndexingTaskCTS.Token);
				}

				await Task.WhenAll(WebDirectoryProcessors);
				Console.WriteLine("Finshed indexing");
				Program.Logger.Information("Finshed indexing");

				if (BrowserContext is not null)
				{
					Program.Logger.Warning($"Closing Browser");
					BrowserContext.Dispose();
					BrowserContext = null;
				}

				if (WebFilesFileSizeQueue.Any())
				{
					TimerStatistics.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;
					Console.WriteLine($"Retrieving filesize of {WebFilesFileSizeQueue.Count} urls");
				}

				await Task.WhenAll(WebFileFileSizeProcessors);

				TimerStatistics.Stop();

				Session.Finished = DateTimeOffset.UtcNow;
				Session.TotalFiles = Session.Root.TotalFiles;
				Session.TotalFileSizeEstimated = Session.Root.TotalFileSize;

				// Replaced WebUtility.UrlDecode with Uri.UnescapeDataString because of issues with Google Drive alternatives (+ sign)
				IEnumerable<string> distinctUrls = Session.Root.AllFileUrls.Distinct().Select(x => Uri.UnescapeDataString(x)).OrderBy(x => x, NaturalSortStringComparer.InvariantCulture);

				if (Session.TotalFiles != distinctUrls.Count())
				{
					Program.Logger.Warning("Indexed files and unique files is not the same, please check results. Found a total of {totalUrls} files resulting in {distinctUrls} urls", Session.TotalFiles, distinctUrls.Count());
				}

				Console.WriteLine(Statistics.GetSessionStats(Session, onlyRedditStats: true, includeExtensions: true));

				if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoUrls &&
					Session.Root.Uri.Host != Constants.GoogleDriveDomain &&
					Session.Root.Uri.Host != Constants.BlitzfilesTechDomain &&
					Session.Root.Uri.Host != Constants.DropboxDomain &&
					Session.Root.Uri.Host != Constants.GoFileIoDomain &&
					Session.Root.Uri.Host != Constants.MediafireDomain &&
					Session.Root.Uri.Host != Constants.PixeldrainDomain)
				{
					if (Session.TotalFiles > 0)
					{
						Program.Logger.Information("Saving URL list to file..");
						Console.WriteLine("Saving URL list to file..");

						try
						{
							string urlsPath = Library.GetOutputFullPath(Session, OpenDirectoryIndexerSettings, "txt");
							File.WriteAllLines(urlsPath, distinctUrls);

							Program.Logger.Information("Saved URL list to file: {path}", urlsPath);
							Console.WriteLine($"Saved URL list to file: {urlsPath}");

							if (OpenDirectoryIndexerSettings.CommandLineOptions.UploadUrls && Session.TotalFiles > 0)
							{
								try
								{
									List<IFileUploadSite> uploadSites = new()
									{
										new Pixeldrain(),
										new ZippyShare(),
										new GoFileIo(),
										new UploadFilesIo(),
										new AnonFiles(),
									};

									foreach (IFileUploadSite uploadSite in uploadSites)
									{
										try
										{
											Console.WriteLine($"Uploading URLs ({FileSizeHelper.ToHumanReadable(new FileInfo(urlsPath).Length)}) with {uploadSite.Name}..");

											IFileUploadSiteFile fileUploaderFile = await uploadSite.UploadFile(HttpClient, urlsPath);
											Program.HistoryLogger.Information("{siteName} URL: {url}", uploadSite.Name, JsonConvert.SerializeObject(fileUploaderFile));
											Program.HistoryLogger.Information("{siteName} full response: {response}", uploadSite.Name, Session.UploadedUrlsResponse);
											Session.UploadedUrlsUrl = fileUploaderFile.Url;
											Console.WriteLine($"Uploaded URLs link: {Session.UploadedUrlsUrl}");
											break;
										}
										catch (Exception ex)
										{
											Program.Logger.Warning("Error uploading URLs: {error}", ex.Message);
										}
									}
								}
								catch (Exception ex)
								{
									Program.Logger.Warning("Error uploading URLs: {error}", ex.Message);
								}
							}
						}
						catch (Exception ex)
						{
							Program.Logger.Error(ex, "Error saving or uploading URLs file: {error}", ex.Message);
						}
					}
					else
					{
						Program.Logger.Information("No URLs to save");
						Console.WriteLine("No URLs to save");
					}
				}

				distinctUrls = null;

				if (OpenDirectoryIndexerSettings.CommandLineOptions.Speedtest &&
					Session.TotalFiles > 0 &&
					Session.Root.Uri.Host != Constants.GoogleDriveDomain &&
					!Session.Root.Uri.Host.EndsWith(Constants.AmazonS3Domain) &&
					Session.Root.Uri.Host != Constants.BlitzfilesTechDomain &&
					Session.Root.Uri.Host != Constants.DropboxDomain &&
					Session.Root.Uri.Host != Constants.GoFileIoDomain &&
					Session.Root.Uri.Host != Constants.GitHubDomain &&
					Session.Root.Uri.Host != Constants.MediafireDomain &&
					Session.Root.Uri.Host != Constants.PixeldrainDomain)
				{
					if (Session.Root.Uri.Scheme == Constants.UriScheme.Http || Session.Root.Uri.Scheme == Constants.UriScheme.Https)
					{
						try
						{
							WebFile biggestFile = Session.Root.AllFiles.OrderByDescending(f => f.FileSize).First();

							Console.WriteLine($"Starting speedtest (10-25 seconds)..");
							Console.WriteLine($"Test file: {FileSizeHelper.ToHumanReadable(biggestFile.FileSize)} {biggestFile.Url}");
							Session.SpeedtestResult = await Library.DoSpeedTestHttpAsync(HttpClient, biggestFile.Url);

							if (Session.SpeedtestResult != null)
							{
								Console.WriteLine($"Finished speedtest. Downloaded: {FileSizeHelper.ToHumanReadable(Session.SpeedtestResult.DownloadedBytes)}, Time: {Session.SpeedtestResult.ElapsedMilliseconds / 1000:F1} s, Speed: {Session.SpeedtestResult.MaxMBsPerSecond:F1} MB/s ({Session.SpeedtestResult.MaxMBsPerSecond * 8:F0} mbit)");
							}
						}
						catch (Exception ex)
						{
							// Give empty speedtest, so it will be reported as Failed
							Session.SpeedtestResult = new Shared.SpeedtestResult();
							Program.Logger.Error(ex, "Speedtest failed");
						}
					}
					else if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
					{
						try
						{
							FluentFTP.AsyncFtpClient ftpClient = FtpParser.FtpClients.FirstOrDefault(c => c.Value.IsConnected).Value;

							FtpParser.CloseAll(exceptFtpClient: ftpClient);

							if (ftpClient != null)
							{

								WebFile biggestFile = Session.Root.AllFiles.OrderByDescending(f => f.FileSize).First();

								Console.WriteLine($"Starting speedtest (10-25 seconds)..");
								Console.WriteLine($"Test file: {FileSizeHelper.ToHumanReadable(biggestFile.FileSize)} {biggestFile.Url}");

								Session.SpeedtestResult = await Library.DoSpeedTestFtpAsync(ftpClient, biggestFile.Url);

								if (Session.SpeedtestResult != null)
								{
									Console.WriteLine($"Finished speedtest. Downloaded: {FileSizeHelper.ToHumanReadable(Session.SpeedtestResult.DownloadedBytes)}, Time: {Session.SpeedtestResult.ElapsedMilliseconds / 1000:F1} s, Speed: {Session.SpeedtestResult.MaxMBsPerSecond:F1} MB/s ({Session.SpeedtestResult.MaxMBsPerSecond * 8:F0} mbit)");
								}
							}
							else
							{
								Console.WriteLine($"Cannot do speedtest because there is no connected FTP client anymore");
							}
						}
						catch (Exception ex)
						{
							// Give empty speedtest, so it will be reported as Failed
							Session.SpeedtestResult = new Shared.SpeedtestResult();
							Program.Logger.Error(ex, "Speedtest failed");
						}
					}
				}
				else
				{
					Program.Logger.Warning("Speedtest skipped because of general service or disabled through command line");
				}

				if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
				{
					FtpParser.CloseAll();
				}

				Program.Logger.Information("Logging sessions stats..");
				try
				{
					string sessionStats = Statistics.GetSessionStats(Session, includeExtensions: true, includeBanner: true);
					Program.Logger.Information(sessionStats);
					Program.HistoryLogger.Information(sessionStats);
					Program.Logger.Information("Logged sessions stats");

					if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoReddit)
					{
						// Also log to screen, when saving links or JSON fails and the logs keep filling by other sessions, this will be saved
						Console.WriteLine(sessionStats);
					}
				}
				catch (Exception ex)
				{
					Program.Logger.Error(ex, "Error logging session stats");
				}

				if (Session.UrlsWithErrors.Any())
				{
					Program.Logger.Information("URLs with errors:");
					Console.WriteLine("URLs with errors:");

					foreach (string urlWithError in Session.UrlsWithErrors.OrderBy(u => u, NaturalSortStringComparer.InvariantCulture))
					{
						Program.Logger.Information(urlWithError);
						Console.WriteLine(urlWithError);
					}
				}

				if (OpenDirectoryIndexerSettings.CommandLineOptions.Json)
				{
					Program.Logger.Information("Saving session to JSON..");
					Console.WriteLine("Saving session to JSON..");

					string jsonPath = Library.GetOutputFullPath(Session, OpenDirectoryIndexerSettings, "json");

					try
					{
						Library.SaveSessionJson(Session, jsonPath);
						Program.Logger.Information("Saved session: {path}", jsonPath);
						Console.WriteLine($"Saved session: {jsonPath}");
					}
					catch (Exception ex)
					{
						Program.Logger.Error(ex, "Error saving session to JSON");
					}
				}

				Program.Logger.Information("Finished indexing!");
				Console.WriteLine("Finished indexing!");

				Program.SetConsoleTitle($"✔ {Program.ConsoleTitle}");

				bool clipboardSuccess = false;

				if (OpenDirectoryIndexerSettings.CommandLineOptions.Clipboard)
				{
					try
					{
						new Clipboard().SetText(Statistics.GetSessionStats(Session, includeExtensions: true, onlyRedditStats: true));
						Console.WriteLine("Copied Reddit stats to clipboard!");
						clipboardSuccess = true;
					}
					catch (Exception ex)
					{
						Program.Logger.Error("Error copying stats to clipboard: {error}", ex.Message);
					}
				}

				if (OpenDirectoryIndexerSettings.CommandLineOptions.Quit)
				{
					Command.KillApplication();
				}
				else
				{
					Console.WriteLine(clipboardSuccess ? "Press ESC to exit!" : "Press ESC to exit! Or C to copy to clipboard and quit!");
				}
			}
			catch (Exception ex)
			{
				Program.Logger.Error(ex, "Error in indexing task");
			}
		});
	}

	/// <summary>
	/// Recursively set parent for all subdirectories
	/// </summary>
	/// <param name="parent"></param>
	private void SetParentDirectories(WebDirectory parent)
	{
		foreach (WebDirectory subdirectory in parent.Subdirectories)
		{
			subdirectory.ParentDirectory = parent;

			SetParentDirectories(subdirectory);
		}
	}

	private void TimerStatistics_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
	{
		if (!ShowStatistics)
		{
			return;
		}

		StringBuilder stringBuilder = new();

		if (WebDirectoriesQueue.Any() || RunningWebDirectoryThreads > 0 || WebFilesFileSizeQueue.Any() || RunningWebFileFileSizeThreads > 0)
		{
			stringBuilder.AppendLine(Statistics.GetSessionStats(Session));
			stringBuilder.AppendLine($"Queue: {Library.FormatWithThousands(WebDirectoriesQueue.Count)} ({RunningWebDirectoryThreads} threads), Queue (filesizes): {Library.FormatWithThousands(WebFilesFileSizeQueue.Count)} ({RunningWebFileFileSizeThreads} threads)");
		}

		string statistics = stringBuilder.ToString();

		if (!string.IsNullOrWhiteSpace(statistics))
		{
			Program.Logger.Warning(statistics);
		}
	}

	private async Task WebDirectoryProcessor(ConcurrentQueue<WebDirectory> queue, string threadName, CancellationToken cancellationToken)
	{
		Program.Logger.Debug("[{thread}] Start", threadName);

		bool maxConnections = false;

		do
		{
			if (RateLimited && RunningWebDirectoryThreads + 1 > 5)
			{
				Program.Logger.Warning($"Decrease threads because of rate limiting");
				break;
			}
			else if (RunningWebDirectoryThreads + 1 > Session.MaxThreads)
			{
				// Don't hog the CPU when queue < threads
				//Program.Log.Information($"Pausing thread because it's there are more threads ({RunningWebDirectoryThreads + 1}) running than wanted ({Session.MaxThreads})");
				await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
				continue;
			}

			Interlocked.Increment(ref RunningWebDirectoryThreads);

			if (queue.TryDequeue(out WebDirectory webDirectory))
			{
				try
				{
					lock (WebDirectoryProcessorInfoLock)
					{
						WebDirectoryProcessorInfo[threadName] = webDirectory;
					}

					if (!Session.ProcessedUrls.Contains(webDirectory.Url))
					{
						Session.ProcessedUrls.Add(webDirectory.Url);
						webDirectory.StartTime = DateTimeOffset.UtcNow;

						Program.Logger.Information("[{thread}] Begin processing {url}", threadName, webDirectory.Url);

						if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
						{
							WebDirectory parsedWebDirectory = await FtpParser.ParseFtpAsync(threadName, webDirectory, OpenDirectoryIndexerSettings.Username, OpenDirectoryIndexerSettings.Password);

							if (webDirectory?.CancellationReason == Constants.Ftp_Max_Connections)
							{
								webDirectory.CancellationReason = null;
								maxConnections = true;

								if (webDirectory.Name == Constants.Root)
								{
									webDirectory.Error = true;
									Interlocked.Decrement(ref RunningWebDirectoryThreads);
									throw new Exception("Error checking FTP because maximum connections reached");
								}

								// Requeue
								Session.ProcessedUrls.Remove(webDirectory.Url);
								queue.Enqueue(webDirectory);

								try
								{
									await FtpParser.FtpClients[threadName].Disconnect(cancellationToken);

									lock (FtpParser.FtpClients)
									{
										FtpParser.FtpClients.Remove(threadName);
									}
								}
								catch (Exception exFtpDisconnect)
								{
									Program.Logger.Error(exFtpDisconnect, "Error disconnecting FTP connection.");
								}
							}

							if (parsedWebDirectory != null)
							{
								DirectoryParser.CheckParsedResults(parsedWebDirectory, Session.Root.Uri.ToString(), true);
								AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
							}
						}
						else if (Session.Root.Uri.Host == Constants.GoogleDriveDomain)
						{
							if (webDirectory.Uri.Segments.Contains("folderview"))
							{
								UrlEncodingParser urlEncodingParserFolderView = new(webDirectory.Url);

								webDirectory.Url = $"https://drive.google.com/drive/folders/{urlEncodingParserFolderView["id"]}";
							}

							string baseUrl = webDirectory.Url;

							UrlEncodingParser urlEncodingParserResourceKey = new(baseUrl);

							GoogleDriveIndexer ??= new(Program.Logger);

							WebDirectory parsedWebDirectory = await GoogleDriveIndexer.IndexAsync(webDirectory, urlEncodingParserResourceKey["resourcekey"]);
							parsedWebDirectory.Url = baseUrl;

							AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
						}
						else
						{
							if (Session.Root.Uri.Host.EndsWith(Constants.AmazonS3Domain) ||
								Session.Root.Uri.Host.EndsWith(Constants.GitHubDomain) ||
								Session.Root.Uri.Host == Constants.BlitzfilesTechDomain ||
								Session.Root.Uri.Host == Constants.DropboxDomain ||
								DirectoryParser.SameHostAndDirectoryFile(Session.Root.Uri, webDirectory.Uri))
							{
								Program.Logger.Debug("[{thread}] Start download '{url}'", threadName, webDirectory.Url);
								Session.TotalHttpRequests++;

								CancellationTokenSource cancellationTokenSource = new();

								cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

								Context pollyContext = new()
								{
									{ "Thread", threadName },
									{ "WebDirectory", webDirectory },
									{ "CancellationTokenSource", cancellationTokenSource }
								};

								await RetryPolicy.ExecuteAsync(async (context, token) => { await ProcessWebDirectoryAsync(threadName, webDirectory, cancellationTokenSource); }, pollyContext, cancellationTokenSource.Token);
							}
							else
							{
								Program.Logger.Warning("[{thread}] Skipped result of '{url}' because it is not the same host or path", threadName, webDirectory.Url);

								Session.Skipped++;
							}
						}

						Program.Logger.Information("[{thread}] Finished processing {url}", threadName, webDirectory.Url);
					}
					else
					{
						//Program.Log.Warning("[{thread}] Skip, already processed: {url}", threadName, webDirectory.Uri);
					}
				}
				catch (Exception ex)
				{
					if (ex is TaskCanceledException taskCanceledException)
					{
						Session.Errors++;
						webDirectory.Error = true;

						if (!Session.UrlsWithErrors.Contains(webDirectory.Url))
						{
							Session.UrlsWithErrors.Add(webDirectory.Url);
						}

						if (webDirectory.ParentDirectory?.Url != null)
						{
							Program.Logger.Error("Skipped processing Url: '{url}' from parent '{parentUrl}'", webDirectory.Url, webDirectory.ParentDirectory.Url);
						}
						else
						{
							Program.Logger.Error("Skipped processing Url: '{url}'", webDirectory.Url);
							Session.Root.Error = true;
						}
					}
					else
					{
						Program.Logger.Error(ex, "Error processing Url: '{url}' from parent '{parentUrl}'", webDirectory.Url, webDirectory.ParentDirectory?.Url);
					}
				}
				finally
				{
					lock (WebDirectoryProcessorInfoLock)
					{
						WebDirectoryProcessorInfo.Remove(threadName);
					}

					if (string.IsNullOrWhiteSpace(webDirectory.CancellationReason))
					{
						webDirectory.Finished = true;
						webDirectory.FinishTime = DateTimeOffset.UtcNow;
					}
				}
			}

			Interlocked.Decrement(ref RunningWebDirectoryThreads);

			if (RunningWebDirectoryThreads > 0)
			{
				// Needed, because of the TryDequeue, no waiting in ConcurrentQueue!
				if (queue.IsEmpty)
				{
					// Don't hog the CPU when queue < threads
					await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
				}
				else
				{
					if (OpenDirectoryIndexerSettings.CommandLineOptions.WaitSecondsBetweenCalls > 0)
					{
						await Task.Delay(TimeSpan.FromSeconds(OpenDirectoryIndexerSettings.CommandLineOptions.WaitSecondsBetweenCalls), cancellationToken);
					}
					else
					{
						await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
					}
				}
			}
		}
		while (!cancellationToken.IsCancellationRequested && (!queue.IsEmpty || RunningWebDirectoryThreads > 0) && !maxConnections);

		Program.Logger.Debug("[{thread}] Finished", threadName);
	}

	private async Task ProcessWebDirectoryAsync(string threadName, WebDirectory webDirectory, CancellationTokenSource cancellationTokenSource)
	{
		if (Session.Parameters.ContainsKey(Constants.Parameters_GdIndex_RootId))
		{
			await Site.GDIndex.GdIndex.GdIndexParser.ParseIndex(HttpClient, webDirectory, string.Empty);
			return;
		}

		if (webDirectory.Uri.Host is Constants.GitHubDomain or Constants.GitHubApiDomain)
		{
			WebDirectory parsedWebDirectory = await GitHubParser.ParseIndex(HttpClient, webDirectory, Session.CommandLineOptions.GitHubToken);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory, processSubdirectories: false);
			return;
		}

		if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.UserAgent))
		{
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(OpenDirectoryIndexerSettings.CommandLineOptions.UserAgent);
		}

		HttpResponseMessage httpResponseMessage = null;

		try
		{
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);
		}
		catch (TaskCanceledException)// when (ex.InnerException is TimeoutException)
		{
			// Retry
			throw;
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
		}

		if (httpResponseMessage?.Headers.Server.FirstOrDefault()?.Product.Name.ToLowerInvariant() == "amazons3")
		{
			WebDirectory parsedWebDirectory = await AmazonS3Parser.ParseIndex(HttpClient, webDirectory, hasHeader: true);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
			return;
		}

		if (httpResponseMessage?.Content.Headers.ContentType?.MediaType == "application/xml")
		{
			string xml = await GetHtml(httpResponseMessage);

			if (xml.Contains("ListBucketResult"))
			{
				WebDirectory parsedWebDirectory = await AmazonS3Parser.ParseIndex(HttpClient, webDirectory, hasHeader: false);
				AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
				return;
			}
		}

		if (httpResponseMessage?.Headers.Server.FirstOrDefault()?.Product.Name.ToLowerInvariant() == "crushftp")
		{
			WebDirectory parsedWebDirectory = await CrushFtpParser.ParseIndex(HttpClient, webDirectory);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
			return;
		}

		if (httpResponseMessage?.StatusCode == HttpStatusCode.Forbidden && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLowerInvariant() == "cloudflare")
		{
			string cloudflareHtml = await GetHtml(httpResponseMessage);

			if (Regex.IsMatch(cloudflareHtml, @"<form class=""challenge-form[^>]*>([\s\S]*?)<\/form>"))
			{
				if (!HttpClient.DefaultRequestHeaders.UserAgent.Any())
				{
					HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
					httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);
				}
			}
		}

		if (httpResponseMessage?.StatusCode == HttpStatusCode.ServiceUnavailable && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLowerInvariant() == "cloudflare")
		{
			if (OpenDirectoryIndexerSettings.CommandLineOptions.NoBrowser)
			{
				Program.Logger.Error("Cloudflare protection detected, --no-browser option active, cannot continue!");
				return;
			}

			bool cloudflareOK = await OpenCloudflareBrowser();

			if (!cloudflareOK)
			{
				Program.Logger.Error("Cloudflare failed!");

				return;
			}
		}

		if (httpResponseMessage?.StatusCode == HttpStatusCode.Forbidden && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLowerInvariant() == "cloudflare")
		{
			string cloudflareHtml = await GetHtml(httpResponseMessage);

			if (Regex.IsMatch(cloudflareHtml, @"<form class=""challenge-form[^>]*>([\s\S]*?)<\/form>"))
			{
				if (!HttpClient.DefaultRequestHeaders.UserAgent.Any())
				{
					HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
					httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);
				}

				if (OpenDirectoryIndexerSettings.CommandLineOptions.NoBrowser)
				{
					Program.Logger.Error("Cloudflare protection detected, --no-browser option active, cannot continue!");
					return;
				}

				bool cloudflareOK = await OpenCloudflareBrowser();

				if (!cloudflareOK)
				{
					Program.Logger.Error("Cloudflare failed!");
				}

				httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);
			}
		}

		if (httpResponseMessage?.StatusCode == HttpStatusCode.Moved || httpResponseMessage?.StatusCode == HttpStatusCode.MovedPermanently)
		{
			if (httpResponseMessage.Headers.Location != null)
			{
				httpResponseMessage = await HttpClient.GetAsync(httpResponseMessage.Headers.Location, cancellationTokenSource.Token);
			}
		}

		if (httpResponseMessage?.Content?.Headers.ContentLength > 20 * Constants.Megabyte)
		{
			ConvertDirectoryToFile(webDirectory, httpResponseMessage);

			return;
		}

		string html = null;

		if (httpResponseMessage?.IsSuccessStatusCode == true)
		{
			SetRootUrl(httpResponseMessage);

			using (Stream htmlStream = await GetHtmlStream(httpResponseMessage))
			{
				if (htmlStream != null)
				{
					html = await GetHtml(htmlStream);
				}
				else
				{
					// CrushFTP when rate limiting / blocking
					if (httpResponseMessage.ReasonPhrase == "BANNED")
					{
						return;
					}

					ConvertDirectoryToFile(webDirectory, httpResponseMessage);

					return;
				}
			}

			if (html.Contains("document.cookie"))
			{
				Regex cookieRegex = new("document\\.cookie\\s?=\\s?\"(?<Cookie>.*?)\"");
				Match cookieRegexMatch = cookieRegex.Match(html);

				if (cookieRegexMatch.Success)
				{
					SocketsHttpHandler.CookieContainer.SetCookies(webDirectory.Uri, cookieRegexMatch.Groups["Cookie"].Value);

					// Retrieve/retry content again with added cookies
					httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

					using (Stream htmlStream = await GetHtmlStream(httpResponseMessage))
					{
						if (htmlStream != null)
						{
							html = await GetHtml(htmlStream);
						}
					}
				}
			}
		}

		if (FirstRequest && (httpResponseMessage == null || !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode) && string.IsNullOrWhiteSpace(html) || html?.Contains("HTTP_USER_AGENT") == true)
		{
			Program.Logger.Warning("First request fails, using Curl fallback User-Agent");
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Curl);
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Program.Logger.Warning("Yes, Curl User-Agent did the trick!");

				SetRootUrl(httpResponseMessage);

				using (Stream htmlStream = await GetHtmlStream(httpResponseMessage))
				{
					if (htmlStream != null)
					{
						html = await GetHtml(htmlStream);
					}
					else
					{
						ConvertDirectoryToFile(webDirectory, httpResponseMessage);

						return;
					}
				}
			}
		}

		if (FirstRequest && (httpResponseMessage == null || !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode) && string.IsNullOrWhiteSpace(html))
		{
			Program.Logger.Warning("First request fails, using Chrome fallback User-Agent");
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Program.Logger.Warning("Yes, Chrome User-Agent did the trick!");

				SetRootUrl(httpResponseMessage);

				using (Stream htmlStream = await GetHtmlStream(httpResponseMessage))
				{
					if (htmlStream != null)
					{
						html = await GetHtml(htmlStream);
					}
					else
					{
						ConvertDirectoryToFile(webDirectory, httpResponseMessage);

						return;
					}
				}
			}
		}

		if (FirstRequest && (httpResponseMessage == null || !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode) && string.IsNullOrWhiteSpace(html))
		{
			Program.Logger.Warning("First request fails, using Chrome fallback User-Agent (with extra headers)");
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
			HttpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html");
			HttpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,nl;q=0.8");
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Program.Logger.Warning("Yes, Chrome User-Agent (with extra headers) did the trick!");

				SetRootUrl(httpResponseMessage);

				using (Stream htmlStream = await GetHtmlStream(httpResponseMessage))
				{
					if (htmlStream != null)
					{
						html = await GetHtml(htmlStream);
					}
					else
					{
						ConvertDirectoryToFile(webDirectory, httpResponseMessage);

						return;
					}
				}
			}
		}

		if (httpResponseMessage is null)
		{
			throw new Exception($"Error retrieving directory listing for {webDirectory.Url}");
		}

		if (!HttpClient.DefaultRequestHeaders.Contains("Referer"))
		{
			HttpClient.DefaultRequestHeaders.Add("Referer", webDirectory.Url);
		}

		bool calibreDetected = false;
		string calibreVersionString = string.Empty;

		if (httpResponseMessage.IsSuccessStatusCode)
		{
			FirstRequest = false;

			List<string> serverHeaders = new();

			if (httpResponseMessage.Headers.Contains("Server"))
			{
				serverHeaders = httpResponseMessage.Headers.GetValues("Server").ToList();

				calibreDetected = serverHeaders.Any(h => h.Contains("calibre"));
			}

			if (calibreDetected)
			{
				string serverHeader = string.Join("/", serverHeaders);
				calibreVersionString = serverHeader;
			}
			else
			{
				if (html == null)
				{
					using (Stream htmlStream = await GetHtmlStream(httpResponseMessage))
					{
						if (htmlStream != null)
						{
							html = await GetHtml(htmlStream);
						}
						else
						{
							ConvertDirectoryToFile(webDirectory, httpResponseMessage);

							return;
						}
					}
				}

				// UNTESTED (cannot find Calibre with this issue)
				const string calibreVersionIdentifier = "CALIBRE_VERSION = \"";
				calibreDetected = html?.Contains(calibreVersionIdentifier) == true;

				if (calibreDetected)
				{
					int calibreVersionIdentifierStart = html.IndexOf(calibreVersionIdentifier);
					calibreVersionString = html.Substring(calibreVersionIdentifierStart, html.IndexOf("\"", ++calibreVersionIdentifierStart));
				}
			}
		}

		if (calibreDetected)
		{
			Version calibreVersion = CalibreParser.ParseVersion(calibreVersionString);

			Console.WriteLine($"Calibre {calibreVersion} detected! I will index it at max 100 books per 30 seconds, else it will break Calibre...");
			Program.Logger.Information("Calibre {calibreVersion} detected! I will index it at max 100 books per 30 seconds, else it will break Calibre...", calibreVersion);

			await CalibreParser.ParseCalibre(HttpClient, httpResponseMessage.RequestMessage.RequestUri, webDirectory, calibreVersion, cancellationTokenSource.Token);

			return;
		}

		if (httpResponseMessage.IsSuccessStatusCode && webDirectory.Url != httpResponseMessage.RequestMessage.RequestUri.ToString())
		{
			// Soft rate limit
			if (httpResponseMessage.RequestMessage.RequestUri.ToString().EndsWith("overload.html"))
			{
				throw new SoftRateLimitException();
			}

			webDirectory.Url = httpResponseMessage.RequestMessage.RequestUri.ToString();
		}

		Uri originalUri = new(webDirectory.Url);
		Program.Logger.Debug("[{thread}] Finish download [HTTP {httpcode}] '{url}', size: {length}", threadName, httpResponseMessage.StatusCode, webDirectory.Url, FileSizeHelper.ToHumanReadable(html?.Length));

		// Process only same site
		if (httpResponseMessage.RequestMessage.RequestUri.Host == Session.Root.Uri.Host)
		{
			int httpStatusCode = (int)httpResponseMessage.StatusCode;

			if (!Session.HttpStatusCodes.ContainsKey(httpStatusCode))
			{
				Session.HttpStatusCodes[httpStatusCode] = 0;
			}

			Session.HttpStatusCodes[httpStatusCode]++;

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				html ??= await GetHtml(httpResponseMessage);

				if (html.Length > Constants.Megabyte)
				{
					Program.Logger.Warning("Large response of {length} for {url}", FileSizeHelper.ToHumanReadable(html.Length), webDirectory.Url);
				}

				Session.TotalHttpTraffic += html.Length;

				WebDirectory parsedWebDirectory = await DirectoryParser.ParseHtml(webDirectory, html, HttpClient, SocketsHttpHandler, httpResponseMessage);

				if (BrowserContext is not null && (parsedWebDirectory.Subdirectories.Any() || parsedWebDirectory.Files.Any()))
				{
					Program.Logger.Warning($"Closing Browser because of successful repsonse");
					BrowserContext.Dispose();
					BrowserContext = null;

					if (Session.MaxThreads != Session.CommandLineOptions.Threads)
					{
						Program.Logger.Warning("Increasing threads back to {threads} because of successful response", Session.CommandLineOptions.Threads);
						Session.MaxThreads = Session.CommandLineOptions.Threads;
					}
				}

				bool processSubdirectories = parsedWebDirectory.Parser != "DirectoryListingModel01";
				AddProcessedWebDirectory(webDirectory, parsedWebDirectory, processSubdirectories);
			}
			else
			{
				if (httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests)
				{
					if (httpResponseMessage.Headers.RetryAfter is RetryConditionHeaderValue retryConditionHeaderValue)
					{
						if (retryConditionHeaderValue.Date is DateTimeOffset dateTimeOffset)
						{
							Program.Logger.Warning("[{thread}] Rate limited on Url '{url}'. Need to wait until {untilDate} ({waitTime:F1}) seconds.", threadName, webDirectory.Url, retryConditionHeaderValue.Date, (DateTimeOffset.UtcNow - dateTimeOffset).TotalSeconds);
							httpResponseMessage.Dispose();
							TimeSpan rateLimitTimeSpan = DateTimeOffset.UtcNow - dateTimeOffset;
							cancellationTokenSource.CancelAfter(rateLimitTimeSpan.Add(TimeSpan.FromMinutes(5)));
							await Task.Delay(rateLimitTimeSpan, cancellationTokenSource.Token);
							throw new SilentException();
						}
						else if (retryConditionHeaderValue.Delta is TimeSpan timeSpan)
						{
							Program.Logger.Warning("[{thread}] Rate limited on Url '{url}'. Need to wait for {waitTime:F1} seconds ({untilDate:HH:mm:ss}).", threadName, webDirectory.Url, timeSpan.TotalSeconds, DateTime.Now.Add(timeSpan));
							httpResponseMessage.Dispose();
							cancellationTokenSource.CancelAfter(timeSpan.Add(TimeSpan.FromMinutes(5)));
							await Task.Delay(timeSpan, cancellationTokenSource.Token);
							throw new SilentException();
						}
						else
						{
							httpResponseMessage.EnsureSuccessStatusCode();
						}
					}
				}
				else
				{
					httpResponseMessage.EnsureSuccessStatusCode();
				}
			}
		}
		else
		{
			Program.Logger.Warning("[{thread}] Skipped result of '{url}' which points to '{redirectedUrl}'", threadName, webDirectory.Url, httpResponseMessage.RequestMessage.RequestUri);
			Session.Skipped++;
		}
	}

	private async Task<bool> OpenCloudflareBrowser()
	{
		Program.Logger.Warning("Cloudflare protection detected, trying to launch browser. Solve protection yourself, indexing will start automatically!");

		BrowserContext browserContext = new(SocketsHttpHandler.CookieContainer, cloudFlare: true);
		bool cloudFlareOK = await browserContext.DoCloudFlareAsync(OpenDirectoryIndexerSettings.Url);

		if (cloudFlareOK)
		{
			Program.Logger.Warning("Cloudflare OK!");

			Program.Logger.Warning("User agent forced to Chrome because of Cloudflare");

			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
		}

		return cloudFlareOK;
	}

	private static void ConvertDirectoryToFile(WebDirectory webDirectory, HttpResponseMessage httpResponseMessage)
	{
		Program.Logger.Warning("Treated '{url}' as file instead of directory ({size})", webDirectory.Url, FileSizeHelper.ToHumanReadable(httpResponseMessage.Content.Headers.ContentLength));

		// Remove it as directory
		webDirectory.ParentDirectory?.Subdirectories.Remove(webDirectory);

		// Add it as a file
		webDirectory.ParentDirectory?.Files.Add(new WebFile
		{
			Url = webDirectory.Url,
			FileName = webDirectory.Name,
			FileSize = httpResponseMessage.Content.Headers.ContentLength ?? Constants.NoFileSize
		});
	}

	private void SetRootUrl(HttpResponseMessage httpResponseMessage)
	{
		if (FirstRequest)
		{
			if (Session.Root.Url != httpResponseMessage.RequestMessage.RequestUri.ToString())
			{
				if (Session.Root.Uri.Host != httpResponseMessage.RequestMessage.RequestUri.Host)
				{
					Program.Logger.Error("Response is NOT from requested host '{badHost}', but from {goodHost}, maybe retry with different user agent, see Command Line options", Session.Root.Uri.Host, httpResponseMessage.RequestMessage.RequestUri.Host);
				}

				Session.Root.Url = httpResponseMessage.RequestMessage.RequestUri.ToString();
				Program.Logger.Warning("Retrieved URL: {url}", Session.Root.Url);
			}
		}
	}

	private static async Task<string> GetHtml(HttpResponseMessage httpResponseMessage)
	{
		FixCharSet(httpResponseMessage);

		return await httpResponseMessage.Content.ReadAsStringAsync();
	}

	private static async Task<string> GetHtml(Stream stream)
	{
		using (StreamReader streamReader = new(stream))
		{
			return await streamReader.ReadToEndAsync();
		}
	}

	/// <summary>
	/// Checks for maximum of 10% control characters, which should not be in HTML
	/// </summary>
	/// <param name="buffer">Buffer to check</param>
	/// <param name="length">Length to check</param>
	/// <returns>True if there is less than 10% control characters</returns>
	private static bool IsHtmlMaybe(char[] buffer, int length)
	{
		int controlChars = buffer.Take(length).Count(c => char.IsControl(c) && c != 10 && c != 13 && c != 9);

		return (100d / (buffer.Length / (double)controlChars)) < 10;
	}

	/// <summary>
	/// Check HttpResponseMessage for HTML, as good as it can
	/// The code below might not be perfect, streams are hard
	/// </summary>
	/// <param name="httpResponseMessage">The HttpResponseMessage to read from</param>
	/// <returns>A checked stream when possible HTML, else null</returns>
	private static async Task<Stream> GetHtmlStream(HttpResponseMessage httpResponseMessage)
	{
		FixCharSet(httpResponseMessage);

		Encoding encoding = Encoding.ASCII;

		string charSet = httpResponseMessage.Content.Headers.ContentType?.CharSet;

		if (!string.IsNullOrWhiteSpace(charSet))
		{
			encoding = Encoding.GetEncoding(charSet);
		}

		// Don't use using tags, it will close the stream for the callee
		MemoryStream responseStream = new();
		StreamWriter streamWriter = new(responseStream);

		using (Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync())
		{
			using (StreamReader streamReader = new(stream, encoding))
			{
				// Check first 10kB for any 'HTML'
				char[] buffer = new char[10 * Constants.Kilobyte];
				int readBytes = await streamReader.ReadBlockAsync(buffer, 0, buffer.Length);

				if (readBytes < buffer.Length)
				{
					Array.Resize(ref buffer, readBytes);
				}

				if (!buffer.Contains('<'))
				{
					return null;
				}
				else if (!IsHtmlMaybe(buffer, readBytes))
				{
					return null;
				}
				else
				{
					Regex htmlRegex = new("<[a-zA-Z0-9] ?([^>]+)>", RegexOptions.IgnoreCase);

					if (!htmlRegex.Match(new string(buffer)).Success)
					{
						return null;
					}
				}

				await streamWriter.WriteAsync(buffer);
				await streamWriter.FlushAsync();

				buffer = new char[Constants.Kilobyte];

				do
				{
					readBytes = await streamReader.ReadBlockAsync(buffer, 0, buffer.Length);

					if (readBytes > 0)
					{
						await streamWriter.WriteAsync(buffer);
						await streamWriter.FlushAsync();
					}
				} while (readBytes > 0);

				streamReader.Close();
			}

			stream.Close();
		}

		await streamWriter.FlushAsync();
		responseStream.Position = 0;

		return responseStream;
	}

	/// <summary>
	/// Check and fix some common bad charsets
	/// </summary>
	/// <param name="httpResponseMessage">Fixed charset</param>
	private static void FixCharSet(HttpResponseMessage httpResponseMessage)
	{
		if (httpResponseMessage.Content.Headers.ContentType?.CharSet?.ToLowerInvariant() == "utf8" ||
			httpResponseMessage.Content.Headers.ContentType?.CharSet?.ToLowerInvariant() == "\"utf-8\"" ||
			httpResponseMessage.Content.Headers.ContentType?.CharSet == "GB1212")
		{
			httpResponseMessage.Content.Headers.ContentType.CharSet = "UTF-8";
		}

		if (httpResponseMessage.Content.Headers.ContentType?.CharSet == "WIN-1251")
		{
			httpResponseMessage.Content.Headers.ContentType.CharSet = "Windows-1251";
		}
	}

	private void AddProcessedWebDirectory(WebDirectory webDirectory, WebDirectory parsedWebDirectory, bool processSubdirectories = true)
	{
		webDirectory.Description = parsedWebDirectory.Description;
		webDirectory.StartTime = parsedWebDirectory.StartTime;
		webDirectory.Files = parsedWebDirectory.Files;
		webDirectory.Finished = parsedWebDirectory.Finished;
		webDirectory.FinishTime = parsedWebDirectory.FinishTime;
		webDirectory.Name = parsedWebDirectory.Name;
		webDirectory.Subdirectories = parsedWebDirectory.Subdirectories;
		webDirectory.Url = parsedWebDirectory.Url;

		if (processSubdirectories)
		{
			foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
			{
				if (!Session.ProcessedUrls.Contains(subdirectory.Url))
				{
					if (subdirectory.Uri.Host != Constants.AmazonS3Domain &&
						subdirectory.Uri.Host != Constants.BlitzfilesTechDomain &&
						subdirectory.Uri.Host != Constants.DropboxDomain &&
						subdirectory.Uri.Host != Constants.GoogleDriveDomain &&
						!DirectoryParser.SameHostAndDirectoryFile(Session.Root.Uri, subdirectory.Uri))
					{
						Program.Logger.Debug("Removed subdirectory '{url}' from parsed webdirectory because it is not the same host", subdirectory.Uri);
					}
					else
					{
						WebDirectoriesQueue.Enqueue(subdirectory);
					}
				}
				else
				{
					//Program.Log.Warning("Url '{url}' already processed, skipping! Source: {sourceUrl}", subdirectory.Url, webDirectory.Url);
				}
			}
		}

		if (parsedWebDirectory.Error && !Session.UrlsWithErrors.Contains(webDirectory.Url))
		{
			Session.UrlsWithErrors.Add(webDirectory.Url);
		}

		if (Session.Root.Uri.Scheme != Constants.UriScheme.Ftp && Session.Root.Uri.Scheme != Constants.UriScheme.Ftps)
		{
			foreach (WebFile webFile in webDirectory.Files.Where(f => (f.FileSize == Constants.NoFileSize && !OpenDirectoryIndexerSettings.CommandLineOptions.FastScan) || OpenDirectoryIndexerSettings.CommandLineOptions.ExactFileSizes))
			{
				WebFilesFileSizeQueue.Enqueue(webFile);
			}
		}
	}

	private async Task WebFileFileSizeProcessor(ConcurrentQueue<WebFile> queue, string threadName, Task[] tasks, CancellationToken cancellationToken)
	{
		Program.Logger.Debug("[{thread}] Start", threadName);

		do
		{
			Interlocked.Increment(ref RunningWebFileFileSizeThreads);

			if (queue.TryDequeue(out WebFile webFile))
			{
				try
				{
					Program.Logger.Debug("Retrieve filesize for: {url}", webFile.Url);

					if (!OpenDirectoryIndexerSettings.DetermimeFileSizeByDownload)
					{
						webFile.FileSize = (await HttpClient.GetUrlFileSizeAsync(webFile.Url)) ?? 0;
					}
					else
					{
						webFile.FileSize = (await HttpClient.GetUrlFileSizeByDownloadingAsync(webFile.Url)) ?? 0;
					}

					Program.Logger.Debug("Retrieved filesize for: {url}", webFile.Url);
				}
				catch (Exception ex)
				{
					Program.Logger.Error(ex, "Error retrieving filesize of Url: '{url}'", webFile.Url);
				}
			}

			Interlocked.Decrement(ref RunningWebFileFileSizeThreads);

			// Needed, because of the TryDequeue, no waiting in ConcurrentQueue!
			if (queue.IsEmpty)
			{
				// Don't hog the CPU when queue < threads
				await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
			}
			else
			{
				await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
			}
		}
		while (!cancellationToken.IsCancellationRequested && (!queue.IsEmpty || RunningWebFileFileSizeThreads > 0 || RunningWebDirectoryThreads > 0 || !tasks.All(t => t.IsCompleted)));

		Program.Logger.Debug("[{thread}] Finished", threadName);
	}
}

public class OpenDirectoryIndexerSettings
{
	public string Url { get; set; }
	public string FileName { get; set; }
	public int Threads { get; set; } = 5;
	public int Timeout { get; set; } = 100;
	public string Username { get; set; }
	public string Password { get; set; }
	public bool DetermimeFileSizeByDownload { get; set; }
	public CommandLineOptions CommandLineOptions { get; set; }
}
