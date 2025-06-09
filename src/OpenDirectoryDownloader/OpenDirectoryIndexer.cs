using AngleSharp.Dom;
using Newtonsoft.Json;
using OpenDirectoryDownloader.Calibre;
using OpenDirectoryDownloader.FileUpload;
using OpenDirectoryDownloader.GoogleDrive;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.AList;
using OpenDirectoryDownloader.Site.AmazonS3;
using OpenDirectoryDownloader.Site.CrushFtp;
using OpenDirectoryDownloader.Site.GitHub;
using Polly;
using Polly.Retry;
using PuppeteerSharp;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using TextCopy;

namespace OpenDirectoryDownloader;

public partial class OpenDirectoryIndexer
{
	public static Session Session { get; set; }
	public static bool ShowStatistics { get; set; } = true;

	public OpenDirectoryIndexerSettings OpenDirectoryIndexerSettings { get; set; }

	public ConcurrentQueue<WebDirectory> WebDirectoriesQueue { get; set; } = new ConcurrentQueue<WebDirectory>();
	public int RunningWebDirectoryThreads;
	public readonly Task[] WebDirectoryProcessors;
	public Dictionary<string, WebDirectory> WebDirectoryProcessorInfo = [];
	public readonly object WebDirectoryProcessorInfoLock = new();

	public ConcurrentQueue<WebFile> WebFilesFileSizeQueue { get; set; } = new ConcurrentQueue<WebFile>();
	public int RunningWebFileFileSizeThreads;
	public readonly Task[] WebFileFileSizeProcessors;

	public CancellationTokenSource IndexingTaskCts { get; set; }
	public Task IndexingTask { get; set; }

	private bool FirstRequest { get; set; } = true;
	private static bool RateLimitedOrConnectionIssues { get; set; }

	private SocketsHttpHandler SocketsHttpHandler { get; set; }
	private HttpClient HttpClient { get; set; }
	public static CookieContainer CookieContainer { get; set; } = new();
	private static List<EncodingInfo> EncodingInfos { get; set; } = [.. Encoding.GetEncodings()];

	private System.Timers.Timer TimerStatistics { get; set; }

	private static readonly Random Jitterer = new();

	private static readonly List<string> KnownErrorPaths =
	[
		"cgi-bin/",
		"lost%2Bfound/"
	];

	private GoogleDriveIndexer GoogleDriveIndexer { get; set; }

	private const int FallbackRetryCount = 4;

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
				else if (retryCount <= FallbackRetryCount && ex is TaskCanceledException taskCanceledException && taskCanceledException.InnerException is TimeoutException timeoutException)
				{
					Program.Logger.Warning("[{thread}] Timeout (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, retryCount, relativeUrl, waitTime);
				}
				else if (ex is HttpRequestException httpRequestException)
				{
					if (OperatingSystem.IsWindows() && ex.InnerException is AuthenticationException)
					{
						Program.Logger.Warning("[{thread}] Please check readme to fix possible TLS 1.3 issue: https://github.com/KoalaBear84/OpenDirectoryDownloader/#tls-errors-windows-10", threadName);
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
						RateLimitedOrConnectionIssues = true;
					}
					else if (httpRequestException.StatusCode == HttpStatusCode.LoopDetected)
					{
						// It could be that a Retry-After header is returned, which should be the seconds of time to wait, but this could be as high as 14.400 which is 4 hours!
						// But a request will probably be successful after just a couple of seconds
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Rate limited / out of capacity (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, retryCount, relativeUrl, waitTime);
						RateLimitedOrConnectionIssues = true;
					}
					else if (ex.Message.Contains("No connection could be made because the target machine actively refused it."))
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Rate limited? (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, retryCount, relativeUrl, waitTime);
						RateLimitedOrConnectionIssues = true;
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
					else if (httpRequestException.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized && retryCount >= 3)
					{
						Program.Logger.Warning("[{thread}] HTTP {httpStatusCode}. Error '{error}' retrieving on try {retryCount}) for '{relativeUrl}'. Skipping..", threadName, httpStatusCode, ex.Message, retryCount, relativeUrl);
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if (retryCount <= FallbackRetryCount)
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
					if (retryCount <= FallbackRetryCount)
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
				RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
				{
					if (!FirstRequest || !sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
					{
						return true;
					}

					string url = OpenDirectoryIndexerSettings.Url;

					try
					{
						string urlHostname = new Uri(url).Host;
						List<string> possibleDnsNames = [];

						Regex commonNameRegex = new("CN=(?<CommonName>[^,]+),.+");

						MatchCollection commonNameRegexMatches = commonNameRegex.Matches(certificate.Subject);
						possibleDnsNames.AddRange(commonNameRegexMatches.Cast<Match>().Select(m => m.Groups["CommonName"].Value));

						if (certificate is X509Certificate2 x509Certificate2)
						{
							Regex dnsNameRegex = new("DNS Name=(?<DnsName>\\S*)");

							foreach (X509SubjectAlternativeNameExtension x509SubjectAlternativeNameExtension in x509Certificate2.Extensions.Where(e => e is X509SubjectAlternativeNameExtension).Cast<X509SubjectAlternativeNameExtension>())
							{
								AsnEncodedData asnEncodedData = new(x509SubjectAlternativeNameExtension.Oid, x509SubjectAlternativeNameExtension.RawData);
								MatchCollection dnsNameRegexMatches = dnsNameRegex.Matches(asnEncodedData.Format(true));
								possibleDnsNames.AddRange(dnsNameRegexMatches.Cast<Match>().Select(m => m.Groups["DnsName"].Value));
							}
						}

						possibleDnsNames = possibleDnsNames.Distinct().ToList();

						if (!possibleDnsNames.Contains(urlHostname, StringComparer.OrdinalIgnoreCase) &&
						    !possibleDnsNames.Any(dnsName => Regex.IsMatch(urlHostname, $"^{Regex.Escape(dnsName).Replace("\\?", ".").Replace("\\*", ".*")}$"))
						   )
						{
							foreach (string possibleDnsName in possibleDnsNames.Distinct().Where(dnsName => dnsName.Contains('.') && Uri.CheckHostName(dnsName) != UriHostNameType.Unknown))
							{
								UriBuilder uriBuilder = new(url)
								{
									Host = possibleDnsName
								};

								Session.PossibleAlternativeUrls.Add(uriBuilder.Uri.ToString());
								Program.Logger.Warning("Correct URL might be: {Url}", uriBuilder.Uri);
							}
						}
					}
					catch (Exception ex)
					{
						Program.Logger.Warning(ex, "Error checking SSL certificate host names for {Url} from {Subject}, please report!", url, certificate.Subject);
					}

					return true;
				}
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

			if (headerName.Equals("cookie", StringComparison.InvariantCultureIgnoreCase))
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

				IndexingTaskCts = new CancellationTokenSource();

				for (int i = 1; i <= WebDirectoryProcessors.Length; i++)
				{
					string processorId = i.ToString().PadLeft(WebDirectoryProcessors.Length.ToString().Length, '0');

					WebDirectoryProcessors[i - 1] = WebDirectoryProcessor(WebDirectoriesQueue, $"P{processorId}", IndexingTaskCts.Token);
				}

				for (int i = 1; i <= WebFileFileSizeProcessors.Length; i++)
				{
					string processorId = i.ToString().PadLeft(WebFileFileSizeProcessors.Length.ToString().Length, '0');

					WebFileFileSizeProcessors[i - 1] = WebFileFileSizeProcessor(WebFilesFileSizeQueue, $"P{processorId}", WebDirectoryProcessors, IndexingTaskCts.Token);
				}

				await Task.WhenAll(WebDirectoryProcessors);
				Console.WriteLine("Finshed indexing");
				Program.Logger.Information("Finshed indexing");

				if (!WebFilesFileSizeQueue.IsEmpty)
				{
					TimerStatistics.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;
					Console.WriteLine($"Retrieving filesize of {WebFilesFileSizeQueue.Count} urls");
				}

				await Task.WhenAll(WebFileFileSizeProcessors);

				TimerStatistics.Stop();

				Session.Finished = DateTimeOffset.UtcNow;
				Session.TotalFiles = Session.Root.TotalFiles;
				Session.TotalFileSizeEstimated = Session.Root.TotalFileSize;

				IEnumerable<string> distinctUrls = Session.Root.AllFileUrls.Distinct().OrderBy(x => x, NaturalSortStringComparer.InvariantCulture);

				if (Session.TotalFiles != distinctUrls.Count())
				{
					Program.Logger.Warning("Indexed files and unique files is not the same, please check results. Found a total of {totalUrls} files resulting in {distinctUrls} urls", Session.TotalFiles, distinctUrls.Count());
				}

				Console.WriteLine(Statistics.GetSessionStats(Session, onlyRedditStats: true, includeExtensions: true));

				bool genericWebsite =
					Session.Root.Uri.Host == Constants.GoogleDriveDomain ||
					Session.Root.Uri.Host == Constants.BlitzfilesTechDomain ||
					Session.Root.Uri.Host == Constants.DropboxDomain ||
					Session.Root.Uri.Host == Constants.GoFileIoDomain ||
					Session.Root.Uri.Host == Constants.MediafireDomain ||
					Session.Root.Uri.Host == Constants.PixeldrainDomain;

				if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoUrls && !genericWebsite)
				{
					if (Session.TotalFiles > 0)
					{
						Program.Logger.Information("Saving URL list to file..");
						Console.WriteLine("Saving URL list to file..");

						try
						{
							string urlsPath = Library.GetOutputFullPath(Session, OpenDirectoryIndexerSettings, "txt");
							List<string> outputUrls = [];
							foreach (string url in distinctUrls)
							{
								string safeUrl = url.Contains("#") ? url.Replace("#", "%23") : url;
								if (Uri.TryCreate(safeUrl, UriKind.Absolute, out Uri uri))
								{
									outputUrls.Add(uri.AbsoluteUri);
								}
								else
								{
									outputUrls.Add(safeUrl);
								}
							}
							File.WriteAllLines(urlsPath, outputUrls);

							Program.Logger.Information("Saved URL list to file: {path}", urlsPath);
							Console.WriteLine($"Saved URL list to file: {urlsPath}");

							if (OpenDirectoryIndexerSettings.CommandLineOptions.UploadUrls && Session.TotalFiles > 0)
							{
								try
								{
									List<IFileUploadSite> uploadSites =
									[
										new GoFileIo(),
										new UploadFilesIo(),
										new AnonFiles(),
									];

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

				if (OpenDirectoryIndexerSettings.CommandLineOptions.Aria2UrlsFile && !genericWebsite)
				{
					if (Session.TotalFiles > 0)
					{
						Program.Logger.Information("Saving aria2 URL list to file..");
						Console.WriteLine("Saving aria2 URL list to file..");
						
						try
						{
							string rootDir = string.Empty;
							string urlDir = string.Empty;
							if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.Aria2RootDir))
							{
								rootDir = OpenDirectoryIndexerSettings.CommandLineOptions.Aria2RootDir.TrimStart('/').TrimEnd('/');
							}

							string urlsPath = Library.GetOutputFullPath(Session, OpenDirectoryIndexerSettings, "txt");

							if (OpenDirectoryIndexerSettings.CommandLineOptions.Aria2UrlDir)
							{
								urlDir = Path.GetFileNameWithoutExtension(urlsPath);
							}

							urlsPath = $"{Path.GetFileNameWithoutExtension(urlsPath)}-aria2.txt";
							using FileStream fileStream = new(urlsPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1024 * 1024);
							using StreamWriter streamWriter = new(fileStream);

							WriteAria2Urls(Session.Root, streamWriter, rootDir, urlDir);

							Program.Logger.Information("Saved aria2 URL list to file: {path}", urlsPath);
							Console.WriteLine($"Saved aria2 URL list to file: {urlsPath}");
						}
						catch (Exception ex)
						{
							Program.Logger.Error(ex, "Error saving aria2 URLs file: {error}", ex.Message);
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

				if (Session.UrlsWithErrors.Count != 0)
				{
					Program.Logger.Information($"URLs with errors ({Session.UrlsWithErrors.Count}):");
					Console.WriteLine($"URLs with errors ({Session.UrlsWithErrors.Count}):");

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

	private static void WriteAria2Urls(WebDirectory webDirectory, StreamWriter streamWriter, string rootDir, string urlDir)
	{
		foreach (WebFile webFile in webDirectory.Files)
			{
				string safeUrl = webFile.Url.Contains("#") ? webFile.Url.Replace("#", "%23") : webFile.Url;
				if (Uri.TryCreate(safeUrl, UriKind.Absolute, out Uri uri))
				{
					streamWriter.WriteLine(uri.AbsoluteUri);
				}
				else
				{
					streamWriter.WriteLine(safeUrl);
				}

				string directory = webFile.Url[0..^webFile.FileName.Length].Replace(Session.Root.Url, string.Empty).TrimEnd('/');
				
				if (!string.IsNullOrWhiteSpace(urlDir))
				{
					directory = Path.Combine(urlDir, directory.TrimStart('/').TrimEnd('/'));
				}

				if (!string.IsNullOrWhiteSpace(rootDir))
				{
					directory = Path.Combine(rootDir, directory.TrimStart('/').TrimEnd('/'));
				}

				streamWriter.WriteLine($"  dir={directory}");
				streamWriter.WriteLine($"  out={webFile.FileName}");
			}

		foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
		{
			WriteAria2Urls(subdirectory, streamWriter, rootDir, urlDir);
		}
	}

	/// <summary>
	/// Recursively set parent for all subdirectories
	/// </summary>
	/// <param name="parent"></param>
	private static void SetParentDirectories(WebDirectory parent)
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

		if (!WebDirectoriesQueue.IsEmpty || RunningWebDirectoryThreads > 0 || !WebFilesFileSizeQueue.IsEmpty || RunningWebFileFileSizeThreads > 0)
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
			if (RateLimitedOrConnectionIssues && RunningWebDirectoryThreads + 1 > 5)
			{
				Program.Logger.Warning($"Decrease threads because of rate limiting");
				break;
			}

			if (RunningWebDirectoryThreads + 1 > Session.MaxThreads)
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

								CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));

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

			if (OpenDirectoryIndexerSettings.CommandLineOptions.WaitSecondsBetweenCalls > 0)
			{
				await Task.Delay(TimeSpan.FromSeconds(OpenDirectoryIndexerSettings.CommandLineOptions.WaitSecondsBetweenCalls), cancellationToken);
			}
			else
			{
				if (RunningWebDirectoryThreads <= 0)
				{
					continue;
				}

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

		if (webDirectory.Parser == Site.HFS.HfsParser.Parser)
		{
			WebDirectory parsedWebDirectory = await Site.HFS.HfsParser.ScanAsync(HttpClient, webDirectory, null);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
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

		if (webDirectory.Parser == AListParser.Parser)
		{
			WebDirectory parsedWebDirectory = await AListParser.ParseIndex(HttpClient, webDirectory);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
			return;
		}

		HttpResponseMessage httpResponseMessage = null;

		try
		{
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token);
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

		if (httpResponseMessage?.Headers.Server.FirstOrDefault()?.Product?.Name.ToLowerInvariant() == "amazons3")
		{
			WebDirectory parsedWebDirectory = await AmazonS3Parser.ParseIndex(HttpClient, webDirectory, hasHeader: true);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
			return;
		}

		if (httpResponseMessage?.Content.Headers.ContentType?.MediaType == "application/xml")
		{
			string xml = await Library.GetHtml(httpResponseMessage);

			if (xml.Contains("ListBucketResult"))
			{
				WebDirectory parsedWebDirectory = await AmazonS3Parser.ParseIndex(HttpClient, webDirectory, hasHeader: false);
				AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
				return;
			}
		}

		if (httpResponseMessage?.Headers.Server.FirstOrDefault()?.Product?.Name.ToLowerInvariant() == "crushftp")
		{
			WebDirectory parsedWebDirectory = await CrushFtpParser.ParseIndex(HttpClient, webDirectory);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
			return;
		}

		if ((httpResponseMessage?.StatusCode == HttpStatusCode.ServiceUnavailable || httpResponseMessage?.StatusCode == HttpStatusCode.Forbidden) && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLowerInvariant() == "cloudflare")
		{
			string cloudflareHtml = await Library.GetHtml(httpResponseMessage);

			if (RegexCloudflareChallengeForm().IsMatch(cloudflareHtml))
			{
				if (HttpClient.DefaultRequestHeaders.UserAgent.Count == 0)
				{
					HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
					httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);
				}

				if (OpenDirectoryIndexerSettings.CommandLineOptions.NoBrowser)
				{
					Program.Logger.Error("Cloudflare protection detected, --no-browser option active, cannot continue!");
					return;
				}

				bool cloudflareOk = await OpenCloudflareBrowser();

				if (!cloudflareOk)
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

		if (httpResponseMessage?.Headers.Server.FirstOrDefault()?.Product?.Name.ToLowerInvariant() == "diamondcdn" && (int?)httpResponseMessage?.StatusCode == 418)
		{
			Program.Logger.Warning("Starting Browser..");
			using BrowserContext browserContext = new(SocketsHttpHandler.CookieContainer);
			await browserContext.InitializeAsync();
			Program.Logger.Warning("Started Browser");

			Program.Logger.Warning("Retrieving HTML through Browser..");
			string browserHtml = await browserContext.GetHtml(webDirectory.Url);
			Program.Logger.Warning("Retrieved HTML through Browser");

			// Transfer cookies to HttpClient, so hopefully the following requests can be done with the help of cookies
			CookieParam[] cookieParams = await browserContext.GetCookiesAsync();

			BrowserContext.AddCookiesToContainer(SocketsHttpHandler.CookieContainer, cookieParams);

			if (Session.MaxThreads != Session.CommandLineOptions.Threads)
			{
				Program.Logger.Warning("Increasing threads back to {threads}", Session.CommandLineOptions.Threads);
				Session.MaxThreads = Session.CommandLineOptions.Threads;
			}

			if (browserHtml != html)
			{
				html = browserHtml;

				// Override to succesfull call
				httpResponseMessage.StatusCode = HttpStatusCode.OK;
			}
		}

		if (httpResponseMessage?.IsSuccessStatusCode == true)
		{
			SetRootUrl(httpResponseMessage);

			if (html is null)
			{
				await using Stream htmlStream = await GetHtmlStream(httpResponseMessage);

				if (htmlStream != null)
				{
					html = await Library.GetHtml(htmlStream);
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

					await using Stream htmlStream = await GetHtmlStream(httpResponseMessage);

					if (htmlStream != null)
					{
						html = await Library.GetHtml(htmlStream);
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

				await using Stream htmlStream = await GetHtmlStream(httpResponseMessage);

				if (htmlStream != null)
				{
					html = await Library.GetHtml(htmlStream);
				}
				else
				{
					ConvertDirectoryToFile(webDirectory, httpResponseMessage);

					return;
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

				await using Stream htmlStream = await GetHtmlStream(httpResponseMessage);

				if (htmlStream != null)
				{
					html = await Library.GetHtml(htmlStream);
				}
				else
				{
					ConvertDirectoryToFile(webDirectory, httpResponseMessage);

					return;
				}
			}
		}

		if (FirstRequest && (httpResponseMessage is not { IsSuccessStatusCode: true } || httpResponseMessage.IsSuccessStatusCode) && string.IsNullOrWhiteSpace(html))
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

				await using Stream htmlStream = await GetHtmlStream(httpResponseMessage);

				if (htmlStream != null)
				{
					html = await Library.GetHtml(htmlStream);
				}
				else
				{
					ConvertDirectoryToFile(webDirectory, httpResponseMessage);

					return;
				}
			}
		}

		if (httpResponseMessage is null)
		{
			throw new Exception($"Error retrieving directory listing (empty/null) for {webDirectory.Url}");
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

			List<string> serverHeaders = [];

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
					await using Stream htmlStream = await GetHtmlStream(httpResponseMessage);

					if (htmlStream != null)
					{
						html = await Library.GetHtml(htmlStream);
					}
					else
					{
						ConvertDirectoryToFile(webDirectory, httpResponseMessage);

						return;
					}
				}

				// UNTESTED (cannot find Calibre with this issue)
				const string calibreVersionIdentifier = "CALIBRE_VERSION = \"";
				calibreDetected = html?.Contains(calibreVersionIdentifier) == true;

				if (calibreDetected)
				{
					int calibreVersionIdentifierStart = html.IndexOf(calibreVersionIdentifier, StringComparison.Ordinal);
					calibreVersionString = html.Substring(calibreVersionIdentifierStart, html.IndexOf("\"", ++calibreVersionIdentifierStart, StringComparison.Ordinal));
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
		if (httpResponseMessage.RequestMessage.RequestUri.Host == Session.Root.Uri.Host ||
			httpResponseMessage.RequestMessage.RequestUri.Host.Equals(Constants.PCloudDomain1) ||
			httpResponseMessage.RequestMessage.RequestUri.Host.Equals(Constants.PCloudDomain2))
		{
			int httpStatusCode = (int)httpResponseMessage.StatusCode;

			Session.HttpStatusCodes.TryAdd(httpStatusCode, 0);

			Session.HttpStatusCodes[httpStatusCode]++;

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				html ??= await Library.GetHtml(httpResponseMessage);

				if (html.Length > Constants.Megabyte)
				{
					Program.Logger.Warning("Large response of {length} for {url}", FileSizeHelper.ToHumanReadable(html.Length), webDirectory.Url);
				}

				Session.TotalHttpTraffic += html.Length;

				WebDirectory parsedWebDirectory = await DirectoryParser.ParseHtml(webDirectory, html, HttpClient, SocketsHttpHandler, httpResponseMessage);

				bool processSubdirectories = parsedWebDirectory.Parser != "DirectoryListingModel01";
				AddProcessedWebDirectory(webDirectory, parsedWebDirectory, processSubdirectories);
			}
			else
			{
				await CheckRetryAfterAndWait(threadName, webDirectory, cancellationTokenSource, httpResponseMessage);

				httpResponseMessage.EnsureSuccessStatusCode();
			}
		}
		else
		{
			Program.Logger.Warning("[{thread}] Skipped result of '{url}' which points to '{redirectedUrl}'", threadName, webDirectory.Url, httpResponseMessage.RequestMessage.RequestUri);
			Session.Skipped++;
		}
	}

	public static async Task CheckRetryAfterAndWait(string threadName, WebDirectory webDirectory, CancellationTokenSource cancellationTokenSource, HttpResponseMessage httpResponseMessage)
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

			if (retryConditionHeaderValue.Delta is TimeSpan timeSpan)
			{
				Program.Logger.Warning("[{thread}] Rate limited on Url '{url}'. Need to wait for {waitTime:F1} seconds ({untilDate:HH:mm:ss}).", threadName, webDirectory.Url, timeSpan.TotalSeconds, DateTime.Now.Add(timeSpan));
				httpResponseMessage.Dispose();
				cancellationTokenSource.CancelAfter(timeSpan.Add(TimeSpan.FromMinutes(5)));
				await Task.Delay(timeSpan, cancellationTokenSource.Token);
				throw new SilentException();
			}
		}
	}

	private async Task<bool> OpenCloudflareBrowser()
	{
		Program.Logger.Warning("Cloudflare protection detected, trying to launch browser. Solve protection yourself, indexing will start automatically!");

		using BrowserContext browserContext = new(SocketsHttpHandler.CookieContainer, cloudFlare: true, debugInfo: false);
		bool cloudFlareOk = await browserContext.DoCloudFlareAsync(OpenDirectoryIndexerSettings.Url);

		if (cloudFlareOk)
		{
			Program.Logger.Warning("Cloudflare OK!");

			Program.Logger.Warning("User agent forced to Chrome because of Cloudflare");

			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
		}

		return cloudFlareOk;
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
			FileSize = httpResponseMessage.Content.Headers.ContentLength
		});
	}

	private void SetRootUrl(HttpResponseMessage httpResponseMessage)
	{
		if (!FirstRequest)
		{
			return;
		}

		if (Session.Root.Url == httpResponseMessage.RequestMessage?.RequestUri?.ToString())
		{
			return;
		}

		if (Session.Root.Uri.Host != httpResponseMessage.RequestMessage?.RequestUri?.Host)
		{
			Program.Logger.Error("Response is NOT from requested host '{badHost}', but from {goodHost}, maybe retry with different user agent, see Command Line options", Session.Root.Uri.Host, httpResponseMessage.RequestMessage.RequestUri.Host);
		}

		Session.Root.Url = httpResponseMessage.RequestMessage.RequestUri.ToString();
		Program.Logger.Warning("Retrieved URL: {url}", Session.Root.Url);
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

		return 100d / (buffer.Length / (double)controlChars) < 10;
	}

	/// <summary>
	/// Check HttpResponseMessage for HTML, as good as it can
	/// The code below might not be perfect, streams are hard
	/// </summary>
	/// <param name="httpResponseMessage">The HttpResponseMessage to read from</param>
	/// <returns>A checked stream when possible HTML, else null</returns>
	private static async Task<Stream> GetHtmlStream(HttpResponseMessage httpResponseMessage)
	{
		Library.FixCharSet(httpResponseMessage);

		Encoding encoding = Encoding.ASCII;

		string charSet = httpResponseMessage.Content.Headers.ContentType?.CharSet;

		if (!string.IsNullOrWhiteSpace(charSet))
		{
			if (EncodingInfos.Any(e => e.Name.Equals(charSet, StringComparison.InvariantCultureIgnoreCase)))
			{
				encoding = Encoding.GetEncoding(charSet);
			}
		}

		// Don't use using tags, it will close the stream for the callee
		MemoryStream responseStream = new();
		StreamWriter streamWriter = new(responseStream, encoding);

		await using Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync();

		using StreamReader streamReader = new(stream, encoding);

		// Check first 10kB for any 'HTML'
		char[] buffer = new char[10 * Constants.Kilobyte];
		int readBytes = await streamReader.ReadBlockAsync(buffer, 0, buffer.Length);

		if (readBytes < buffer.Length)
		{
			Array.Resize(ref buffer, readBytes);
		}

		if (readBytes == 0)
		{
			return responseStream;
		}

		if (!buffer.Contains('<'))
		{
			return null;
		}

		if (!IsHtmlMaybe(buffer, readBytes))
		{
			return null;
		}

		Regex htmlRegex = HtmlRegex();

		if (!htmlRegex.Match(new string(buffer)).Success)
		{
			return null;
		}

		await streamWriter.WriteAsync(buffer, 0, buffer.Length);
		await streamWriter.FlushAsync();

		buffer = new char[Constants.Kilobyte];

		do
		{
			readBytes = await streamReader.ReadBlockAsync(buffer, 0, buffer.Length);

			if (readBytes <= 0)
			{
				continue;
			}

			await streamWriter.WriteAsync(buffer, 0, readBytes);
			await streamWriter.FlushAsync();
		} while (readBytes > 0);

		streamReader.Close();

		stream.Close();

		await streamWriter.FlushAsync();
		responseStream.Seek(0, SeekOrigin.Begin);

		return responseStream;
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

				//Program.Log.Warning("Url '{url}' already processed, skipping! Source: {sourceUrl}", subdirectory.Url, webDirectory.Url);
			}
		}

		if (parsedWebDirectory.Error && !Session.UrlsWithErrors.Contains(webDirectory.Url))
		{
			Session.UrlsWithErrors.Add(webDirectory.Url);
		}

		if (Session.Root.Uri.Scheme != Constants.UriScheme.Ftp && Session.Root.Uri.Scheme != Constants.UriScheme.Ftps)
		{
			foreach (WebFile webFile in webDirectory.Files.Where(f => f.FileSize is null && !OpenDirectoryIndexerSettings.CommandLineOptions.FastScan || OpenDirectoryIndexerSettings.CommandLineOptions.ExactFileSizes))
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

					if (!OpenDirectoryIndexerSettings.DetermineFileSizeByDownload)
					{
						webFile.FileSize = await HttpClient.GetUrlFileSizeAsync(webFile.Url) ?? 0;
					}
					else
					{
						webFile.FileSize = await HttpClient.GetUrlFileSizeByDownloadingAsync(webFile.Url) ?? 0;
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

	[GeneratedRegex("<[a-zA-Z0-9] ?([^>]+)>", RegexOptions.IgnoreCase, "en-US")]
	private static partial Regex HtmlRegex();

	[GeneratedRegex(@"<form (?:class|id)=""challenge-form[^>]*>([\s\S]*?)<\/form>")]
	private static partial Regex RegexCloudflareChallengeForm();
}

public class OpenDirectoryIndexerSettings
{
	public string Url { get; set; }
	public string FileName { get; set; }
	public int Threads { get; set; } = 5;
	public int Timeout { get; set; } = 100;
	public string Username { get; set; }
	public string Password { get; set; }
	public bool DetermineFileSizeByDownload { get; set; }
	public CommandLineOptions CommandLineOptions { get; set; }
}
