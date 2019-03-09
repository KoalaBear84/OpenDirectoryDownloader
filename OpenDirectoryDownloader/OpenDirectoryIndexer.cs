using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Calibre;
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    public class OpenDirectoryIndexer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Logger HistoryLogger = LogManager.GetLogger("historyFile");

        public Session Session { get; set; }

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
        private readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(5, // 16 seconds
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(retryAttempt),
                //sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(retryAttempt * 2),
                //sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, span, retryCount, context) =>
                {
                    // TODO: Correct URL, via context?
                    Logger.Warn($"Error {ex.Message} retrieving on try {retryCount} for url '{context}'. Waiting {span.TotalSeconds} seconds.");
                }
            );

        private const string UserAgent_Curl = "curl/7.55.1";
        private const string UserAgent_Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/73.0.3642.0 Safari/537.36";

        public OpenDirectoryIndexer(OpenDirectoryIndexerSettings openDirectoryIndexerSettings)
        {
            OpenDirectoryIndexerSettings = openDirectoryIndexerSettings;

            HttpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            HttpClient = new HttpClient(HttpClientHandler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Fix encoding issue with "windows-1251"
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            WebDirectoryProcessors = new Task[OpenDirectoryIndexerSettings.Threads];
            WebFileFileSizeProcessors = new Task[OpenDirectoryIndexerSettings.Threads];

            //HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent_Curl);
            //HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent_Chrome);
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

            if (Session.Root.Uri.Host == "drive.google.com")
            {
                Logger.Warn("Google Drive scanning is limited to 10 directories per second!");
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

                    //var taskSource = Task.Run(() => TaskProducer(taskQueue));

                    for (int i = 1; i <= OpenDirectoryIndexerSettings.Threads; i++)
                    {
                        string processorId = i.ToString();

                        WebDirectoryProcessors[i - 1] = WebDirectoryProcessor(WebDirectoriesQueue, $"Processor {processorId}", IndexingTaskCTS.Token);
                    }

                    for (int i = 1; i <= OpenDirectoryIndexerSettings.Threads; i++)
                    {
                        string processorId = i.ToString();

                        WebFileFileSizeProcessors[i - 1] = WebFileFileSizeProcessor(WebFilesFileSizeQueue, $"Processor {processorId}", IndexingTaskCTS.Token, WebDirectoryProcessors);
                    }

                    //await taskSource;
                    //IndexingTaskCTS.CancelAfter(TimeSpan.FromSeconds(2));

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

                    if (!OpenDirectoryIndexerSettings.CommandLineOptions.NoUrls)
                    {
                        Logger.Info("Saving URL list to file...");
                        Console.WriteLine("Saving URL list to file...");

                        try
                        {
                            string fileUrls = string.Join(Environment.NewLine, Session.Root.AllFileUrls.Distinct());
                            string urlsFileName = $"{PathHelper.GetValidPath(Session.Root.Url)}.txt";
                            string urlsPath = $"{Library.GetApplicationPath()}{ urlsFileName}";
                            Logger.Info("String joined");
                            File.WriteAllText(urlsPath, fileUrls);
                            Logger.Info($"Saved URL list to file: {urlsFileName}");
                            Console.WriteLine($"Saved URL list to file: {urlsFileName}");

                            if (OpenDirectoryIndexerSettings.CommandLineOptions.UploadUrls)
                            {
                                Console.WriteLine("Uploading URLs...");

                                UploadFilesFile uploadFilesFile = await UploadFileIo.UploadFile(HttpClient, urlsPath);

                                HistoryLogger.Info($"uploadfiles.io: {JsonConvert.SerializeObject(uploadFilesFile)}");

                                Session.UploadedUrlsUrl = uploadFilesFile.Url.ToString();

                                Console.WriteLine($"Uploaded URLs: {Session.UploadedUrlsUrl}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                    }

                    if (OpenDirectoryIndexerSettings.CommandLineOptions.Speedtest && Session.TotalFiles > 0 && (Session.Root.Uri.Scheme == "https" || Session.Root.Uri.Scheme == "http"))
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
                        Logger.Warn("Only a speedtest for HTTP(S)");
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

                    Console.Title = $"✔ {Console.Title}";

                    if (OpenDirectoryIndexerSettings.CommandLineOptions.Quit)
                    {
                        Command.KillApplication();
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

        private async Task WebDirectoryProcessor(ConcurrentQueue<WebDirectory> queue, string name, CancellationToken token)
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
                            Logger.Info($"[{name}] Begin processing {webDirectory.Url}");

                            if (Session.Root.Uri.Scheme == "ftp")
                            {
                                WebDirectory parsedWebDirectory = await FtpParser.ParseFtpAsync(name, webDirectory);
                                AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
                            }
                            else
                            if (Session.Root.Uri.Host == "drive.google.com")
                            {
                                string baseUrl = webDirectory.Url;

                                WebDirectory parsedWebDirectory = await GoogleDriveIndexer.IndexAsync(webDirectory);
                                parsedWebDirectory.Url = baseUrl;

                                AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
                            }
                            else
                            {
                                if (webDirectory.Uri.Host == Session.Root.Uri.Host && webDirectory.Uri.LocalPath.StartsWith(Session.Root.Uri.LocalPath))
                                {
                                    Logger.Debug($"[{name}] Start download '{webDirectory.Url}'");
                                    Session.TotalHttpRequests++;

                                    await RetryPolicy.ExecuteAsync(async () =>
                                    {
                                        webDirectory.StartTime = DateTimeOffset.UtcNow;

                                        HttpResponseMessage httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url);
                                        string html = null;

                                        if (httpResponseMessage.IsSuccessStatusCode)
                                        {
                                            html = await GetHtml(httpResponseMessage);
                                        }

                                        if (FirstRequest && !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode && string.IsNullOrWhiteSpace(html))
                                        {
                                            Logger.Warn("First request fails, using Curl fallback User-Agent");
                                            HttpClient.DefaultRequestHeaders.UserAgent.Clear();
                                            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent_Curl);
                                            httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url);

                                            if (httpResponseMessage.IsSuccessStatusCode)
                                            {
                                                html = await GetHtml(httpResponseMessage);
                                                Logger.Warn("Yes, this Curl User-Agent did the trick!");
                                            }
                                        }

                                        if (FirstRequest && !httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.IsSuccessStatusCode && string.IsNullOrWhiteSpace(html))
                                        {
                                            Logger.Warn("First request fails, using Chrome fallback User-Agent");
                                            HttpClient.DefaultRequestHeaders.UserAgent.Clear();
                                            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent_Chrome);
                                            httpResponseMessage = await HttpClient.GetAsync(webDirectory.Url);

                                            if (httpResponseMessage.IsSuccessStatusCode)
                                            {
                                                html = await GetHtml(httpResponseMessage);
                                                Logger.Warn("Yes, the Chrome User-Agent did the trick!");
                                            }
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
                                                AddProcessedWebDirectory(webDirectory, parsedWebDirectory);
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
                                    });
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
                            Logger.Warn($"[{name}] Skip, already processed: {webDirectory.Uri}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error processing Url: '{webDirectory.Url}' from parent '{webDirectory.ParentDirectory.Url}'");

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
                    }
                }

                Interlocked.Decrement(ref RunningWebDirectoryThreads);

                // Needed!
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }
            while (!token.IsCancellationRequested && (!queue.IsEmpty || RunningWebDirectoryThreads > 0));

            Logger.Debug($"Finished [{name}]");
        }

        private static async Task<string> GetHtml(HttpResponseMessage httpResponseMessage)
        {
            if (httpResponseMessage.Content.Headers.ContentType?.CharSet == "utf8")
            {
                httpResponseMessage.Content.Headers.ContentType.CharSet = "UTF-8";
            }

            return await httpResponseMessage.Content.ReadAsStringAsync();
        }

        private void AddProcessedWebDirectory(WebDirectory webDirectory, WebDirectory parsedWebDirectory)
        {
            webDirectory.Description = parsedWebDirectory.Description;
            webDirectory.StartTime = parsedWebDirectory.StartTime;
            webDirectory.Files = parsedWebDirectory.Files;
            webDirectory.Finished = parsedWebDirectory.Finished;
            webDirectory.Name = parsedWebDirectory.Name;
            webDirectory.Subdirectories = parsedWebDirectory.Subdirectories;
            webDirectory.Url = parsedWebDirectory.Url;

            foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
            {
                if (!Session.ProcessedUrls.Contains(subdirectory.Url))
                {
                    if (subdirectory.Uri.Host != Session.Root.Uri.Host || !subdirectory.Uri.LocalPath.StartsWith(Session.Root.Uri.LocalPath))
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
                    Logger.Warn($"Url '{subdirectory.Url}' already processed, skipping! Source: {webDirectory.Url}");
                }
            }

            if (parsedWebDirectory.Error && !Session.UrlsWithErrors.Contains(webDirectory.Url))
            {
                Session.UrlsWithErrors.Add(webDirectory.Url);
            }

            webDirectory.Files.RemoveAll(f =>
            {
                Uri uri = new Uri(f.Url);
                return uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "ftp" && (uri.Host != Session.Root.Uri.Host || !uri.LocalPath.StartsWith(Session.Root.Uri.LocalPath));
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
        public bool DetermimeFileSizeByDownload { get; set; }
        public CommandLineOptions CommandLineOptions { get; set; }
    }
}
