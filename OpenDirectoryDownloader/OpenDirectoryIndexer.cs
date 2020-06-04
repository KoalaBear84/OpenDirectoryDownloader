using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Calibre;
using OpenDirectoryDownloader.FileUpload;
using OpenDirectoryDownloader.GoogleDrive;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared.Models;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    public class OpenDirectoryIndexer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Logger HistoryLogger = LogManager.GetLogger("historyFile");

        public static Session Session { get; set; }

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
        private OpenDirectoryIndexerSettings OpenDirectoryIndexerSettings { get; set; }
        private System.Timers.Timer TimerStatistics { get; set; }

        private static readonly Random Jitterer = new Random();

        private readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(100,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, retryAttempt))) + TimeSpan.FromMilliseconds(Jitterer.Next(0, 200)),
                onRetry: (ex, span, retryCount, context) =>
                {
                    WebDirectory webDirectory = context["WebDirectory"] as WebDirectory;

                    string relativeUrl = webDirectory.Uri.PathAndQuery;

                    if (ex is HttpRequestException httpRequestException)
                    {
                        if (ex.Message.Contains("503 (Service Temporarily Unavailable)") || ex.Message.Contains("503 (Service Unavailable)") || ex.Message.Contains("429 (Too Many Requests)"))
                        {
                            Logger.Warn($"[{context["Processor"]}] Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
                        }
                        else if (ex.Message.Contains("No connection could be made because the target machine actively refused it."))
                        {
                            Logger.Warn($"[{context["Processor"]}] Rate limited? (try {retryCount}). Url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
                        }
                        else if (ex.Message.Contains("404 (Not Found)") || ex.Message == "No such host is known.")
                        {
                            Logger.Warn($"[{context["Processor"]}] Error {ex.Message} retrieving on try {retryCount} for url '{relativeUrl}'. Skipping..");
                            (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                        }
                        else if ((ex.Message.Contains("401 (Unauthorized)") || ex.Message.Contains("403 (Forbidden)")) && retryCount >= 3)
                        {
                            Logger.Warn($"[{context["Processor"]}] Error {ex.Message} retrieving on try {retryCount} for url '{relativeUrl}'. Skipping..");
                            (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                        }
                        else if (retryCount <= 4)
                        {
                            Logger.Warn($"[{context["Processor"]}] Error {ex.Message} retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
                        }
                        else
                        {
                            (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                        }

                    }
                    else if (webDirectory.Uri.Segments.LastOrDefault() == "cgi-bin/")
                    {
                        (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                    }
                    else
                    {
                        if (retryCount <= 4)
                        {
                            Logger.Warn($"[{context["Processor"]}] Error {ex.Message} retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
                        }
                        else
                        {
                            (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                        }
                    }
                }
            );

        public OpenDirectoryIndexer(OpenDirectoryIndexerSettings openDirectoryIndexerSettings)
        {
            OpenDirectoryIndexerSettings = openDirectoryIndexerSettings;

            HttpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            HttpClient = new HttpClient(HttpClientHandler)
            {
                Timeout = TimeSpan.FromSeconds(OpenDirectoryIndexerSettings.Timeout)
            };

            if (!string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.Username) && !string.IsNullOrWhiteSpace(OpenDirectoryIndexerSettings.Password))
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
                        Name = "ROOT",
                        Url = OpenDirectoryIndexerSettings.Url
                    }
                };
            }

            if (Session.Root.Uri.Host == Constants.GoogleDriveDomain)
            {
                Logger.Warn("Google Drive scanning is limited to 9 directories per second!");
            }

            if (Session.Root.Uri.Scheme == "ftp")
            {
                Logger.Warn("Retrieving FTP software!");
                // TODO: Replace with library?
                Logger.Warn(await FtpParser.GetFtpServerInfo(Session.Root));
                //AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
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

                        WebDirectoryProcessors[i - 1] = WebDirectoryProcessor(WebDirectoriesQueue, $"Processor {processorId}", IndexingTaskCTS.Token);
                    }

                    for (int i = 1; i <= WebFileFileSizeProcessors.Length; i++)
                    {
                        string processorId = i.ToString();

                        WebFileFileSizeProcessors[i - 1] = WebFileFileSizeProcessor(WebFilesFileSizeQueue, $"Processor {processorId}", IndexingTaskCTS.Token, WebDirectoryProcessors);
                    }

                    await Task.WhenAll(WebDirectoryProcessors);
                    Console.WriteLine("Finshed indexing");
                    Logger.Info("Finshed indexing");

                    if (Session.Root.Uri.Scheme == "ftp")
                    {
                        FtpParser.CloseAll();
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

                    if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoUrls && Session.Root.Uri.Host != Constants.GoogleDriveDomain)
                    {
                        if (Session.TotalFiles > 0)
                        {
                            Logger.Info("Saving URL list to file...");
                            Console.WriteLine("Saving URL list to file...");

                            string scansPath = Library.GetScansPath();

                            try
                            {
                                string fileUrls = string.Join(Environment.NewLine, Session.Root.AllFileUrls.Distinct());

                                string urlsFileName = $"{Library.CleanUriToFilename(Session.Root.Uri)}.txt";
                                string urlsPath = Path.Combine(scansPath, urlsFileName);
                                Logger.Info("String joined");
                                File.WriteAllText(urlsPath, fileUrls);
                                Logger.Info($"Saved URL list to file: {urlsFileName}");
                                Console.WriteLine($"Saved URL list to file: {urlsFileName}");

                                if (OpenDirectoryIndexerSettings.CommandLineOptions.UploadUrls && Session.TotalFiles > 0)
                                {
                                    Console.WriteLine("Uploading URLs...");

                                    //UploadFilesFile uploadFilesFile = await UploadFileIo.UploadFile(HttpClient, urlsPath);
                                    //HistoryLogger.Info($"uploadfiles.io: {JsonConvert.SerializeObject(uploadFilesFile)}");
                                    //Session.UploadedUrlsUrl = uploadFilesFile.Url.ToString();

                                    GoFilesFile uploadedFile = await GoFileIo.UploadFile(HttpClient, urlsPath);
                                    HistoryLogger.Info($"goFile.io: {JsonConvert.SerializeObject(uploadedFile)}");
                                    Session.UploadedUrlsUrl = uploadedFile.Url.ToString();

                                    Console.WriteLine($"Uploaded URLs: {Session.UploadedUrlsUrl}");
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

                    if (OpenDirectoryIndexerSettings.CommandLineOptions.Speedtest && Session.Root.Uri.Host != Constants.GoogleDriveDomain)
                    {
                        if (Session.TotalFiles > 0)
                        {
                            if (Session.Root.Uri.Scheme == "https" || Session.Root.Uri.Scheme == "http")
                            {
                                try
                                {
                                    WebFile biggestFile = Session.Root.AllFiles.OrderByDescending(f => f.FileSize).First();

                                    Console.WriteLine($"Starting speedtest (10-25 seconds)...");
                                    Console.WriteLine($"Test file: {FileSizeHelper.ToHumanReadable(biggestFile.FileSize)} {biggestFile.Url}");
                                    Session.SpeedtestResult = await Library.DoSpeedTestAsync(HttpClient, biggestFile.Url);
                                    Console.WriteLine($"Finished speedtest. Downloaded: {FileSizeHelper.ToHumanReadable(Session.SpeedtestResult.DownloadedBytes)}, Time: {Session.SpeedtestResult.ElapsedMiliseconds / 1000:F1} s, Speed: {Session.SpeedtestResult.MaxMBsPerSecond:F1} MB/s ({Session.SpeedtestResult.MaxMBsPerSecond * 8:F0} mbit)");
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, "Speedtest failed");
                                }
                            }
                            else
                            {
                                Logger.Warn($"Only a speedtest for HTTP(S), not '{Session.Root.Uri.Scheme}'");
                            }
                        }
                    }

                    Logger.Info("Logging sessions stats...");
                    try
                    {
                        string sessionStats = Statistics.GetSessionStats(Session, includeExtensions: true);
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
                        Logger.Info("Save session to JSON");
                        Console.WriteLine("Save session to JSON");

                        try
                        {
                            Library.SaveSessionJson(Session);
                            Logger.Info($"Saved session: {PathHelper.GetValidPath(Session.Root.Url)}.json");
                            Console.WriteLine($"Saved session: {PathHelper.GetValidPath(Session.Root.Url)}.json");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                    }

                    Logger.Info("Finished indexing!");
                    Console.WriteLine("Finished indexing!");

                    Program.SetConsoleTitle($"✔ {Program.ConsoleTitle}");

                    if (OpenDirectoryIndexerSettings.CommandLineOptions.Quit)
                    {
                        Command.KillApplication();
                    }
                    else
                    {
                        Console.WriteLine("Press ESC to exit! Or C to copy to clipboard and quit!");
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
            if (WebDirectoriesQueue.Any() || RunningWebDirectoryThreads > 0)
            {
                Logger.Warn(Statistics.GetSessionStats(Session));
                Logger.Warn($"Queue: {Library.FormatWithThousands(WebDirectoriesQueue.Count)}, Queue (filesizes): {Library.FormatWithThousands(WebFilesFileSizeQueue.Count)}");
            }

            if (WebFilesFileSizeQueue.Any() || RunningWebFileFileSizeThreads > 0)
            {
                Logger.Warn($"Remaing urls to retrieve filesize: {Library.FormatWithThousands(WebFilesFileSizeQueue.Count)}");
            }
        }

        private async Task WebDirectoryProcessor(ConcurrentQueue<WebDirectory> queue, string name, CancellationToken cancellationToken)
        {
            Logger.Debug($"Start [{name}]");

            do
            {
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

                            if (Session.Root.Uri.Scheme == "ftp")
                            {
                                WebDirectory parsedWebDirectory = await FtpParser.ParseFtpAsync(name, webDirectory);
                                AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
                            }
                            else
                            if (Session.Root.Uri.Host == Constants.GoogleDriveDomain)
                            {
                                string baseUrl = webDirectory.Url;

                                WebDirectory parsedWebDirectory = await GoogleDriveIndexer.IndexAsync(webDirectory);
                                parsedWebDirectory.Url = baseUrl;

                                AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
                            }
                            else
                            {
                                if (SameHostAndDirectory(Session.Root.Uri, webDirectory.Uri))
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

                                    await RetryPolicy.ExecuteAsync(async (context, token) => { await ProcessWebDirectoryAsync(name, webDirectory, cancellationTokenSource.Token); }, pollyContext, cancellationTokenSource.Token);
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
                            if (webDirectory.ParentDirectory?.Url != null)
                            {
                                Logger.Warn($"Skipped processing Url: '{webDirectory.Url}' from parent '{webDirectory.ParentDirectory.Url}'");
                            }
                            else
                            {
                                Logger.Warn($"Skipped processing Url: '{webDirectory.Url}'");
                                Session.Root.Error = true;
                            }
                        }
                        else
                        {
                            Logger.Error(ex, $"Error processing Url: '{webDirectory.Url}' from parent '{webDirectory.ParentDirectory?.Url}'");
                        }

                        Session.Errors++;

                        if (!Session.UrlsWithErrors.Contains(webDirectory.Url))
                        {
                            Session.UrlsWithErrors.Add(webDirectory.Url);
                        }
                    }
                    finally
                    {
                        lock (WebDirectoryProcessorInfoLock)
                        {
                            WebDirectoryProcessorInfo.Remove(name);
                        }

                        webDirectory.Finished = true;
                        webDirectory.FinishTime = DateTimeOffset.UtcNow;
                    }
                }

                Interlocked.Decrement(ref RunningWebDirectoryThreads);

                // Needed!
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
            while (!cancellationToken.IsCancellationRequested && (!queue.IsEmpty || RunningWebDirectoryThreads > 0));

            Logger.Debug($"Finished [{name}]");
        }

        private bool SameHostAndDirectory(Uri baseUri, Uri checkUri)
        {
            string checkUrlWithoutFileName = checkUri.LocalPath;
            string checkUrlFileName = Path.GetFileName(checkUri.ToString());

            if (!string.IsNullOrWhiteSpace(checkUrlFileName))
            {
                checkUrlWithoutFileName = checkUri.LocalPath.Replace(checkUrlFileName, string.Empty);
            }

            string baseUrlWithoutFileName = baseUri.LocalPath;
            string baseUrlFileName = Path.GetFileName(baseUri.ToString());

            if (!string.IsNullOrWhiteSpace(baseUrlFileName))
            {
                baseUrlWithoutFileName = baseUri.LocalPath.Replace(baseUrlFileName, string.Empty);
            }

            return baseUri.ToString() == checkUri.ToString() || (baseUri.Host == checkUri.Host && (
                checkUri.LocalPath.StartsWith(baseUri.LocalPath) ||
                checkUri.LocalPath.StartsWith(baseUrlWithoutFileName) ||
                baseUri.LocalPath.StartsWith(checkUrlWithoutFileName)
            ));
        }

        private async Task ProcessWebDirectoryAsync(string name, WebDirectory webDirectory, CancellationToken cancellationToken)
        {
            HttpResponseMessage httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url, cancellationToken);
            string html = null;

            if (httpResponseMessage.IsSuccessStatusCode)
            {
                SetRootUrl(httpResponseMessage);

                html = await GetHtml(httpResponseMessage);
            }

            if (FirstRequest && !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode && string.IsNullOrWhiteSpace(html))
            {
                Logger.Warn("First request fails, using Curl fallback User-Agent");
                HttpClient.DefaultRequestHeaders.UserAgent.Clear();
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Curl);
                httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url);

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    SetRootUrl(httpResponseMessage);

                    html = await GetHtml(httpResponseMessage);
                    Logger.Warn("Yes, the Curl User-Agent did the trick!");
                }
            }

            if (FirstRequest && !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode && string.IsNullOrWhiteSpace(html))
            {
                Logger.Warn("First request fails, using Chrome fallback User-Agent");
                HttpClient.DefaultRequestHeaders.UserAgent.Clear();
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
                httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url);

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    SetRootUrl(httpResponseMessage);

                    html = await GetHtml(httpResponseMessage);
                    Logger.Warn("Yes, the Chrome User-Agent did the trick!");
                }
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
                        html = await GetHtml(httpResponseMessage);
                    }

                    // UNTESTED (cannot find or down Calibre with this issue)
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

                await CalibreParser.ParseCalibre(HttpClient, httpResponseMessage.RequestMessage.RequestUri, webDirectory, calibreVersion);

                return;
            }

            if (httpResponseMessage.IsSuccessStatusCode && webDirectory.Url != httpResponseMessage.RequestMessage.RequestUri.ToString())
            {
                webDirectory.Url = httpResponseMessage.RequestMessage.RequestUri.ToString();
            }

            Uri originalUri = new Uri(webDirectory.Url);
            Logger.Debug($"[{name}] Finish download '{webDirectory.Url}'");

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
                    if (html == null)
                    {
                        html = await GetHtml(httpResponseMessage);
                    }

                    Session.TotalHttpTraffic += html.Length;

                    WebDirectory parsedWebDirectory = await DirectoryParser.ParseHtml(webDirectory, html, HttpClient);
                    bool processSubdirectories = parsedWebDirectory.Parser != "DirectoryListingModel01";
                    AddProcessedWebDirectory(webDirectory, parsedWebDirectory, processSubdirectories);
                }
                else
                {
                    Session.Errors++;
                    webDirectory.Error = true;

                    if (!Session.UrlsWithErrors.Contains(webDirectory.Url))
                    {
                        Session.UrlsWithErrors.Add(webDirectory.Url);
                    }

                    httpResponseMessage.EnsureSuccessStatusCode();
                }
            }
            else
            {
                Logger.Warn($"[{name}] Skipped result of '{webDirectory.Url}' which points to '{httpResponseMessage.RequestMessage.RequestUri}'");
                Session.Skipped++;
            }
        }

        private void SetRootUrl(HttpResponseMessage httpResponseMessage)
        {
            if (FirstRequest)
            {
                if (Session.Root.Url != httpResponseMessage.RequestMessage.RequestUri.ToString())
                {
                    Session.Root.Url = httpResponseMessage.RequestMessage.RequestUri.ToString();
                    Logger.Warn($"Retrieved URL: {Session.Root.Url}");
                }
            }
        }

        private static async Task<string> GetHtml(HttpResponseMessage httpResponseMessage)
        {
            if (httpResponseMessage.Content.Headers.ContentType?.CharSet == "utf8")
            {
                httpResponseMessage.Content.Headers.ContentType.CharSet = "UTF-8";
            }

            return await httpResponseMessage.Content.ReadAsStringAsync();
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
                        if (subdirectory.Uri.Host != Constants.GoogleDriveDomain && !SameHostAndDirectory(Session.Root.Uri, subdirectory.Uri))
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

            webDirectory.Files.RemoveAll(f =>
            {
                Uri uri = new Uri(f.Url);

                if (uri.Host == Constants.GoogleDriveDomain)
                {
                    return false;
                }

                return (uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "ftp") || uri.Host != Session.Root.Uri.Host || !SameHostAndDirectory(uri, Session.Root.Uri);
            });

            foreach (WebFile webFile in webDirectory.Files.Where(f => f.FileSize == -1 || OpenDirectoryIndexerSettings.CommandLineOptions.ExactFileSizes))
            {
                WebFilesFileSizeQueue.Enqueue(webFile);
            }
        }

        private async Task WebFileFileSizeProcessor(ConcurrentQueue<WebFile> queue, string name, CancellationToken token, Task[] tasks)
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

                // Needed!
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
            while (!token.IsCancellationRequested && (!queue.IsEmpty || RunningWebFileFileSizeThreads > 0 || RunningWebDirectoryThreads > 0 || !tasks.All(t => t.IsCompleted)));

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
}
