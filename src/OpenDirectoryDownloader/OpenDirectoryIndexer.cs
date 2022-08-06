using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Calibre;
using OpenDirectoryDownloader.FileUpload;
using OpenDirectoryDownloader.GoogleDrive;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Site.AmazonS3;
using OpenDirectoryDownloader.Site.GitHub;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TextCopy;

namespace OpenDirectoryDownloader;

public class OpenDirectoryIndexer
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static readonly Logger HistoryLogger = LogManager.GetLogger("historyFile");

	public static Session Session { get; set; }
	public static bool ShowStatistics { get; set; } = true;

	public OpenDirectoryIndexerSettings OpenDirectoryIndexerSettings { get; set; }

	public ConcurrentQueue<WebDirectory> WebDirectoriesQueue { get; set; } = new ConcurrentQueue<WebDirectory>();
	public int RunningWebDirectoryThreads;
	public Task[] WebDirectoryProcessors;
	public Dictionary<string, WebDirectory> WebDirectoryProcessorInfo = new Dictionary<string, WebDirectory>();
	public readonly object WebDirectoryProcessorInfoLock = new object();

	public ConcurrentQueue<WebFile> WebFilesFileSizeQueue { get; set; } = new ConcurrentQueue<WebFile>();
	public int RunningWebFileFileSizeThreads;
	public Task[] WebFileFileSizeProcessors;

	public CancellationTokenSource IndexingTaskCTS { get; set; }
	public Task IndexingTask { get; set; }

	private bool FirstRequest { get; set; } = true;

	private HttpClientHandler HttpClientHandler { get; set; }
	private HttpClient HttpClient { get; set; }
	private System.Timers.Timer TimerStatistics { get; set; }

	private static readonly Random Jitterer = new Random();

	private static readonly List<string> KnownErrorPaths = new List<string>()
	{
		"cgi-bin/",
		"lost%2Bfound/"
	};

	private readonly AsyncRetryPolicy RetryPolicy = Policy
		.Handle<Exception>()
		.WaitAndRetryAsync(100,
			sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, retryAttempt))) + TimeSpan.FromMilliseconds(Jitterer.Next(0, 200)),
			onRetry: (ex, span, retryCount, context) =>
			{
				WebDirectory webDirectory = context["WebDirectory"] as WebDirectory;

				string relativeUrl = webDirectory.Uri.PathAndQuery;

				if (ex is SilentException)
				{
					// Silence
				}
				else if (ex is SoftRateLimitException)
				{
					Logger.Warn($"[{context["Processor"]}] Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
				}
				else if (ex is HttpRequestException httpRequestException)
				{
					int httpStatusCode = (int)httpRequestException.StatusCode;

					if (KnownErrorPaths.Contains(webDirectory.Uri.Segments.LastOrDefault()))
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Cancelling known error on try {retryCount} for url '{relativeUrl}'.");
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if (httpRequestException.StatusCode == HttpStatusCode.ServiceUnavailable || httpRequestException.StatusCode == HttpStatusCode.TooManyRequests)
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
					}
					else if (ex.Message.Contains("No connection could be made because the target machine actively refused it."))
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Rate limited? (try {retryCount}). Url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
					}
					else if (!Session.GDIndex && (httpRequestException.StatusCode == HttpStatusCode.NotFound || ex.Message == "No such host is known."))
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Error \'{ex.Message}\' retrieving on try {retryCount} for url '{relativeUrl}'. Skipping..");
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if ((httpRequestException.StatusCode == HttpStatusCode.Forbidden || httpRequestException.StatusCode == HttpStatusCode.Unauthorized) && retryCount >= 3)
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Error \'{ex.Message}\' retrieving on try {retryCount} for url '{relativeUrl}'. Skipping..");
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
					else if (retryCount <= 4)
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Error \'{GetExceptionWithInner(ex)}\' retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
					}
					else
					{
						Logger.Warn($"[{context["Processor"]}] HTTP {httpStatusCode}. Cancelling on try {retryCount} for url '{relativeUrl}'.");
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
					}
				}
				else
				{
					if (retryCount <= 4)
					{
						Logger.Warn($"[{context["Processor"]}] Error \'{GetExceptionWithInner(ex)}\' retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
					}
					else
					{
						Logger.Warn($"[{context["Processor"]}] Cancelling on try {retryCount} for url '{relativeUrl}'.");
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

		CookieContainer cookieContainer = new CookieContainer();

		HttpClientHandler = new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
			CookieContainer = cookieContainer
		};

		if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyAddress))
		{
			WebProxy webProxy = new WebProxy
			{
				Address = new Uri(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyAddress),
			};

			if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyUsername) || !string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyPassword))
			{
				webProxy.Credentials = new NetworkCredential(OpenDirectoryIndexerSettings.CommandLineOptions.ProxyUsername, OpenDirectoryIndexerSettings.CommandLineOptions.ProxyPassword);
			}

			HttpClientHandler.Proxy = webProxy;
		}

		HttpClient = new HttpClient(HttpClientHandler)
		{
			Timeout = TimeSpan.FromSeconds(OpenDirectoryIndexerSettings.Timeout)
		};

		HttpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
		HttpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");

		foreach (string customHeader in OpenDirectoryIndexerSettings.CommandLineOptions.Header)
		{
			if (!customHeader.Contains(':'))
			{
				Logger.Warn($"Invalid header specified: '{customHeader}' should contain the header name and value, separated by a colon (:). Header will be ignored.");
				continue;
			}

			string[] splitHeader = customHeader.Split(':');
			splitHeader[1] = splitHeader[1].TrimStart();

			if (splitHeader.Length != 2)
			{
				Logger.Warn($"Invalid header specified: '{customHeader}' should only contain a single colon (:), for separating header name and value. Header will be ignored.");
				continue;
			}

			if (splitHeader[0].ToString().ToLower() == "cookie")
			{
				string[] cookies = splitHeader[1].Split(';');

				foreach (string cookie in cookies)
				{
					string[] splitCookie = cookie.Split('=');

					if (splitCookie.Length != 2)
					{
						Logger.Warn($"Invalid cookie found: '{cookie}' should contain a cookie name and value, separated by '='. Cookie will be ignored.");
						continue;
					}

					Logger.Warn($"Adding cookie: name={splitCookie[0]}, value={splitCookie[1]}");
					cookieContainer.Add(new Uri(OpenDirectoryIndexerSettings.Url), new Cookie(splitCookie[0], splitCookie[1]));
				}
			}
			else
			{
				HttpClient.DefaultRequestHeaders.Add(splitHeader[0], splitHeader[1]);
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
				MaxThreads = OpenDirectoryIndexerSettings.Threads
			};
		}

		Session.MaxThreads = OpenDirectoryIndexerSettings.Threads;

		if (Session.Root.Uri.Host == Constants.GoogleDriveDomain)
		{
			Logger.Warn("Google Drive scanning is limited to 9 directories per second!");
		}

		if (Session.Root.Uri.Host == Constants.GitHubDomain)
		{
			Logger.Warn("GitHub scanning has a very low rate limiting of 60 directories/requests per hour!");

			if (Session.MaxThreads != 1)
			{
				Session.MaxThreads = 1;
				Logger.Warn($"Reduce threads to 1 because of GitHub");
			}
		}

		if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
		{
			Logger.Warn("Retrieving FTP(S) software!");

			if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
			{
				if (Session.Root.Uri.Port == -1)
				{
					Logger.Warn("Using default port (990) for FTPS");

					UriBuilder uriBuilder = new UriBuilder(Session.Root.Uri)
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

			Logger.Warn(serverInfo);
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
					string processorId = i.ToString();

					WebDirectoryProcessors[i - 1] = WebDirectoryProcessor(WebDirectoriesQueue, $"P{processorId}", IndexingTaskCTS.Token);
				}

				for (int i = 1; i <= WebFileFileSizeProcessors.Length; i++)
				{
					string processorId = i.ToString();

					WebFileFileSizeProcessors[i - 1] = WebFileFileSizeProcessor(WebFilesFileSizeQueue, $"P{processorId}", WebDirectoryProcessors, IndexingTaskCTS.Token);
				}

				await Task.WhenAll(WebDirectoryProcessors);
				Console.WriteLine("Finshed indexing");
				Logger.Info("Finshed indexing");

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
				IEnumerable<string> distinctUrls = Session.Root.AllFileUrls.Distinct().Select(i => Uri.UnescapeDataString(i));

				if (Session.TotalFiles != distinctUrls.Count())
				{
					Logger.Error($"Indexed files and unique files is not the same, please check results. Found a total of {Session.TotalFiles} files resulting in {distinctUrls.Count()} urls");
				}

				Console.WriteLine(Statistics.GetSessionStats(Session, onlyRedditStats: true, includeExtensions: true));

				if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoUrls &&
					Session.Root.Uri.Host != Constants.GoogleDriveDomain &&
					Session.Root.Uri.Host != Constants.BlitzfilesTechDomain &&
					Session.Root.Uri.Host != Constants.DropboxDomain &&
					Session.Root.Uri.Host != Constants.GitHubDomain &&
					Session.Root.Uri.Host != Constants.GoFileIoDomain &&
					Session.Root.Uri.Host != Constants.MediafireDomain &&
					Session.Root.Uri.Host != Constants.PixeldrainDomain)
				{
					if (Session.TotalFiles > 0)
					{
						Logger.Info("Saving URL list to file..");
						Console.WriteLine("Saving URL list to file..");

						try
						{
							string urlsPath = Library.GetOutputFullPath(Session, OpenDirectoryIndexerSettings, "txt");
							File.WriteAllLines(urlsPath, distinctUrls);

							Logger.Info($"Saved URL list to file: {urlsPath}");
							Console.WriteLine($"Saved URL list to file: {urlsPath}");

							if (OpenDirectoryIndexerSettings.CommandLineOptions.UploadUrls && Session.TotalFiles > 0)
							{
								try
								{
									List<IFileUploadSite> uploadSites = new List<IFileUploadSite>()
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
											HistoryLogger.Info($"{uploadSite.Name}: {JsonConvert.SerializeObject(fileUploaderFile)}");
											Session.UploadedUrlsUrl = fileUploaderFile.Url;
											Console.WriteLine($"Uploaded URLs link: {Session.UploadedUrlsUrl}");
											break;
										}
										catch (Exception ex)
										{
											Logger.Warn($"Error uploading URLs: {ex.Message}");
										}
									}
								}
								catch (Exception ex)
								{
									Logger.Warn($"Error uploading URLs: {ex.Message}");
								}
							}
						}
						catch (Exception ex)
						{
							Logger.Error(ex);
						}
					}
					else
					{
						Logger.Info("No URLs to save");
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
							Logger.Error(ex, "Speedtest failed");
						}
					}
					else if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
					{
						try
						{
							FluentFTP.FtpClient ftpClient = FtpParser.FtpClients.FirstOrDefault(c => c.Value.IsConnected).Value;

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
							Logger.Error(ex, "Speedtest failed");
						}
					}
				}

				if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
				{
					FtpParser.CloseAll();
				}

				Logger.Info("Logging sessions stats..");
				try
				{
					string sessionStats = Statistics.GetSessionStats(Session, includeExtensions: true, includeBanner: true);
					Logger.Info(sessionStats);
					HistoryLogger.Info(sessionStats);
					Logger.Info("Logged sessions stats");

					if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoReddit)
					{
						// Also log to screen, when saving links or JSON fails and the logs keep filling by other sessions, this will be saved
						Console.WriteLine(sessionStats);
					}
				}
				catch (Exception ex)
				{
					Logger.Error(ex);
				}

				if (Session.UrlsWithErrors.Any())
				{
					Logger.Info("URLs with errors:");
					Console.WriteLine("URLs with errors:");

					foreach (string urlWithError in Session.UrlsWithErrors.OrderBy(u => u))
					{
						Logger.Info(urlWithError);
						Console.WriteLine(urlWithError);
					}
				}

				if (OpenDirectoryIndexerSettings.CommandLineOptions.Json)
				{
					Logger.Info("Saving session to JSON..");
					Console.WriteLine("Saving session to JSON..");

					string jsonPath = Library.GetOutputFullPath(Session, OpenDirectoryIndexerSettings, "json");

					try
					{
						Library.SaveSessionJson(Session, jsonPath);
						Logger.Info($"Saved session: {jsonPath}");
						Console.WriteLine($"Saved session: {jsonPath}");
					}
					catch (Exception ex)
					{
						Logger.Error(ex);
					}
				}

				Logger.Info("Finished indexing!");
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
						Logger.Error($"Error copying stats to clipboard: {ex.Message}");
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
				Logger.Error(ex);
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

		StringBuilder stringBuilder = new StringBuilder();

		if (WebDirectoriesQueue.Any() || RunningWebDirectoryThreads > 0 || WebFilesFileSizeQueue.Any() || RunningWebFileFileSizeThreads > 0)
		{
			stringBuilder.AppendLine(Statistics.GetSessionStats(Session));
			stringBuilder.AppendLine($"Queue: {Library.FormatWithThousands(WebDirectoriesQueue.Count)} ({RunningWebDirectoryThreads} threads), Queue (filesizes): {Library.FormatWithThousands(WebFilesFileSizeQueue.Count)} ({RunningWebFileFileSizeThreads} threads)");
		}

		string statistics = stringBuilder.ToString();

		if (!string.IsNullOrWhiteSpace(statistics))
		{
			Logger.Warn(statistics);
		}
	}

	private async Task WebDirectoryProcessor(ConcurrentQueue<WebDirectory> queue, string name, CancellationToken cancellationToken)
	{
		Logger.Debug($"Start [{name}]");

		bool maxConnections = false;

		do
		{
			if (RunningWebDirectoryThreads + 1 > Session.MaxThreads)
			{
				Logger.Info($"Stopped thread because it's there are more threads ({RunningWebDirectoryThreads + 1}) running than wanted ({Session.MaxThreads})");
				break;
			}

			Interlocked.Increment(ref RunningWebDirectoryThreads);

			if (queue.TryDequeue(out WebDirectory webDirectory))
			{
				try
				{
					lock (WebDirectoryProcessorInfoLock)
					{
						WebDirectoryProcessorInfo[name] = webDirectory;
					}

					if (!Session.ProcessedUrls.Contains(webDirectory.Url))
					{
						Session.ProcessedUrls.Add(webDirectory.Url);
						webDirectory.StartTime = DateTimeOffset.UtcNow;

						Logger.Info($"[{name}] Begin processing {webDirectory.Url}");

						if (Session.Root.Uri.Scheme == Constants.UriScheme.Ftp || Session.Root.Uri.Scheme == Constants.UriScheme.Ftps)
						{
							WebDirectory parsedWebDirectory = await FtpParser.ParseFtpAsync(name, webDirectory, OpenDirectoryIndexerSettings.Username, OpenDirectoryIndexerSettings.Password);

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
									await FtpParser.FtpClients[name].DisconnectAsync(cancellationToken);

									lock (FtpParser.FtpClients)
									{
										FtpParser.FtpClients.Remove(name);
									}
								}
								catch (Exception exFtpDisconnect)
								{
									Logger.Error(exFtpDisconnect, "Error disconnecting FTP connection.");
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
								UrlEncodingParser urlEncodingParserFolderView = new UrlEncodingParser(webDirectory.Url);

								webDirectory.Url = $"https://drive.google.com/drive/folders/{urlEncodingParserFolderView["id"]}";
							}

							string baseUrl = webDirectory.Url;

							UrlEncodingParser urlEncodingParserResourceKey = new UrlEncodingParser(baseUrl);

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
								Logger.Debug($"[{name}] Start download '{webDirectory.Url}'");
								Session.TotalHttpRequests++;

								CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

								cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

								Context pollyContext = new Context
								{
									{ "Processor", name },
									{ "WebDirectory", webDirectory },
									{ "CancellationTokenSource", cancellationTokenSource }
								};

								await RetryPolicy.ExecuteAsync(async (context, token) => { await ProcessWebDirectoryAsync(name, webDirectory, cancellationTokenSource); }, pollyContext, cancellationTokenSource.Token);
							}
							else
							{
								Logger.Warn($"[{name}] Skipped result of '{webDirectory.Url}' because it is not the same host or path");

								Session.Skipped++;
							}
						}

						Logger.Info($"[{name}] Finished processing {webDirectory.Url}");
					}
					else
					{
						//Logger.Warn($"[{name}] Skip, already processed: {webDirectory.Uri}");
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
							Logger.Error($"Skipped processing Url: '{webDirectory.Url}' from parent '{webDirectory.ParentDirectory.Url}'");
						}
						else
						{
							Logger.Error($"Skipped processing Url: '{webDirectory.Url}'");
							Session.Root.Error = true;
						}
					}
					else
					{
						Logger.Error(ex, $"Error processing Url: '{webDirectory.Url}' from parent '{webDirectory.ParentDirectory?.Url}'");
					}
				}
				finally
				{
					lock (WebDirectoryProcessorInfoLock)
					{
						WebDirectoryProcessorInfo.Remove(name);
					}

					if (string.IsNullOrWhiteSpace(webDirectory.CancellationReason))
					{
						webDirectory.Finished = true;
						webDirectory.FinishTime = DateTimeOffset.UtcNow;
					}
				}
			}

			Interlocked.Decrement(ref RunningWebDirectoryThreads);

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
		while (!cancellationToken.IsCancellationRequested && (!queue.IsEmpty || RunningWebDirectoryThreads > 0) && !maxConnections);

		Logger.Debug($"Finished [{name}]");
	}

	private async Task ProcessWebDirectoryAsync(string name, WebDirectory webDirectory, CancellationTokenSource cancellationTokenSource)
	{
		if (Session.Parameters.ContainsKey(Constants.Parameters_GdIndex_RootId))
		{
			await Site.GDIndex.GdIndex.GdIndexParser.ParseIndex(HttpClient, webDirectory, string.Empty);
			return;
		}

		if (webDirectory.Uri.Host is Constants.GitHubDomain or Constants.GitHubApiDomain)
		{
			WebDirectory parsedWebDirectory = await GitHubParser.ParseIndex(HttpClient, webDirectory);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
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
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error retrieving directory listing for {webDirectory.Url}");
		}

		if (httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLower() == "amazons3")
		{
			WebDirectory parsedWebDirectory = await AmazonS3Parser.ParseIndex(HttpClient, webDirectory);
			AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
			return;
		}

		if (httpResponseMessage?.StatusCode == HttpStatusCode.Forbidden && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLower() == "cloudflare")
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

		if (httpResponseMessage?.StatusCode == HttpStatusCode.ServiceUnavailable && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLower() == "cloudflare")
		{
			if (OpenDirectoryIndexerSettings.CommandLineOptions.NoBrowser)
			{
				Logger.Error("Cloudflare protection detected, --no-browser option active, cannot continue!");
				return;
			}

			bool cloudflareOK = await OpenCloudflareBrowser();

			if (!cloudflareOK)
			{
				Logger.Error("Cloudflare failed!");

				return;
			}
		}

		if (httpResponseMessage?.StatusCode == HttpStatusCode.Forbidden && httpResponseMessage.Headers.Server.FirstOrDefault()?.Product.Name.ToLower() == "cloudflare")
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
					Logger.Error("Cloudflare protection detected, --no-browser option active, cannot continue!");
					return;
				}

				bool cloudflareOK = await OpenCloudflareBrowser();

				if (!cloudflareOK)
				{
					Logger.Error("Cloudflare failed!");
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
					ConvertDirectoryToFile(webDirectory, httpResponseMessage);

					return;
				}
			}
		}

		if (FirstRequest && (httpResponseMessage == null || !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode) && string.IsNullOrWhiteSpace(html) || html?.Contains("HTTP_USER_AGENT") == true)
		{
			Logger.Warn("First request fails, using Curl fallback User-Agent");
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Curl);
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Logger.Warn("Yes, Curl User-Agent did the trick!");

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
			Logger.Warn("First request fails, using Chrome fallback User-Agent");
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Logger.Warn("Yes, Chrome User-Agent did the trick!");

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
			Logger.Warn("First request fails, using Chrome fallback User-Agent");
			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
			HttpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html");
			HttpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,nl;q=0.8");
			httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationTokenSource.Token);

			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Logger.Warn("Yes, Chrome User-Agent (with extra headers) did the trick!");

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

			List<string> serverHeaders = new List<string>();

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
			Logger.Info($"Calibre {calibreVersion} detected! I will index it at max 100 books per 30 seconds, else it will break Calibre...");

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

		Uri originalUri = new Uri(webDirectory.Url);
		Logger.Debug($"[{name}] Finish download [HTTP {(int)httpResponseMessage.StatusCode}] '{webDirectory.Url}', size: {FileSizeHelper.ToHumanReadable(html?.Length)}");

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
					Logger.Warn($"Large response of {FileSizeHelper.ToHumanReadable(html.Length)} for {webDirectory.Url}");
				}

				Session.TotalHttpTraffic += html.Length;

				WebDirectory parsedWebDirectory = await DirectoryParser.ParseHtml(webDirectory, html, HttpClient, httpResponseMessage);
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
							Logger.Warn($"[{name}] Rate limited on Url '{webDirectory.Url}'. Need to wait until {retryConditionHeaderValue.Date} ({(DateTimeOffset.UtcNow - dateTimeOffset).TotalSeconds:F1}) seconds.");
							httpResponseMessage.Dispose();
							TimeSpan rateLimitTimeSpan = DateTimeOffset.UtcNow - dateTimeOffset;
							cancellationTokenSource.CancelAfter(rateLimitTimeSpan.Add(TimeSpan.FromMinutes(5)));
							await Task.Delay(rateLimitTimeSpan, cancellationTokenSource.Token);
							throw new SilentException();
						}
						else if (retryConditionHeaderValue.Delta is TimeSpan timeSpan)
						{
							Logger.Warn($"[{name}] Rate limited on Url '{webDirectory.Url}'. Need to wait for {timeSpan.TotalSeconds:F1} seconds ({DateTime.Now.Add(timeSpan):HH:mm:ss}).");
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
			Logger.Warn($"[{name}] Skipped result of '{webDirectory.Url}' which points to '{httpResponseMessage.RequestMessage.RequestUri}'");
			Session.Skipped++;
		}
	}

	private async Task<bool> OpenCloudflareBrowser()
	{
		Logger.Warn("Cloudflare protection detected, trying to launch browser. Solve protection yourself, indexing will start automatically!");

		BrowserContext browserContext = new BrowserContext(OpenDirectoryIndexerSettings.Url, HttpClientHandler.CookieContainer);
		bool cloudFlareOK = await browserContext.DoAsync();

		if (cloudFlareOK)
		{
			Logger.Warn("Cloudflare OK!");

			Logger.Warn("User agent forced to Chrome because of Cloudflare");

			HttpClient.DefaultRequestHeaders.UserAgent.Clear();
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
		}

		return cloudFlareOK;
	}

	private static void ConvertDirectoryToFile(WebDirectory webDirectory, HttpResponseMessage httpResponseMessage)
	{
		Logger.Warn($"Treated {webDirectory.Url} as file instead of directory ({FileSizeHelper.ToHumanReadable(httpResponseMessage.Content.Headers.ContentLength)})");

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
					Logger.Error($"Response is NOT from requested host ({Session.Root.Uri.Host}), but from {httpResponseMessage.RequestMessage.RequestUri.Host}, maybe retry with different user agent, see Command Line options");
				}

				Session.Root.Url = httpResponseMessage.RequestMessage.RequestUri.ToString();
				Logger.Warn($"Retrieved URL: {Session.Root.Url}");
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
		using (StreamReader streamReader = new StreamReader(stream))
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
		MemoryStream responseStream = new MemoryStream();
		StreamWriter streamWriter = new StreamWriter(responseStream);

		using (Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync())
		{
			using (StreamReader streamReader = new StreamReader(stream, encoding))
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
					Regex htmlRegex = new Regex("<[a-zA-Z0-9] ?([^>]+)>", RegexOptions.IgnoreCase);

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
		if (httpResponseMessage.Content.Headers.ContentType?.CharSet?.ToLower() == "utf8" ||
			httpResponseMessage.Content.Headers.ContentType?.CharSet?.ToLower() == "\"utf-8\"" ||
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
						!subdirectory.Uri.Host.EndsWith(Constants.GitHubDomain) &&
						subdirectory.Uri.Host != Constants.GoogleDriveDomain &&
						!DirectoryParser.SameHostAndDirectoryFile(Session.Root.Uri, subdirectory.Uri))
					{
						Logger.Debug($"Removed subdirectory {subdirectory.Uri} from parsed webdirectory because it is not the same host");
					}
					else
					{
						WebDirectoriesQueue.Enqueue(subdirectory);
					}
				}
				else
				{
					//Logger.Warn($"Url '{subdirectory.Url}' already processed, skipping! Source: {webDirectory.Url}");
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

	private async Task WebFileFileSizeProcessor(ConcurrentQueue<WebFile> queue, string name, Task[] tasks, CancellationToken cancellationToken)
	{
		Logger.Debug($"Start [{name}]");

		do
		{
			Interlocked.Increment(ref RunningWebFileFileSizeThreads);

			if (queue.TryDequeue(out WebFile webFile))
			{
				try
				{
					Logger.Debug($"Retrieve filesize for: {webFile.Url}");

					if (!OpenDirectoryIndexerSettings.DetermimeFileSizeByDownload)
					{
						webFile.FileSize = (await HttpClient.GetUrlFileSizeAsync(webFile.Url)) ?? 0;
					}
					else
					{
						webFile.FileSize = (await HttpClient.GetUrlFileSizeByDownloadingAsync(webFile.Url)) ?? 0;
					}

					Logger.Debug($"Retrieved filesize for: {webFile.Url}");
				}
				catch (Exception ex)
				{
					Logger.Error(ex, $"Error retrieving filesize of Url: '{webFile.Url}'");
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

		Logger.Debug($"Finished [{name}]");
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
