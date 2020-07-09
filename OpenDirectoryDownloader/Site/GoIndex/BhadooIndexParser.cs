using NLog;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GoIndex.Bhadoo
{
    /// <summary>
    /// Similar to GoIndex
    /// </summary>
    public static class BhadooIndexParser
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string FolderMimeType = "application/vnd.google-apps.folder";
        const string Parser = "BhadooIndex";

        public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
        {
            if (OpenDirectoryIndexer.Session.MaxThreads > 1)
            {
                throw new FriendlyException($"{Parser} can only scan at maximum of 1 thread, please call with -t 1 or --threads 1");
            }

            try
            {
                if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
                {
                    Console.WriteLine($"{Parser} will always be indexed with only 1 thread, else you will run into problems and errors.");
                    Logger.Info($"{Parser} will always be indexed with only 1 thread, else you will run into problems and errors.");
                    OpenDirectoryIndexer.Session.MaxThreads = 1;

                    Console.WriteLine("Check if password is needed (unsupported currently)...");
                    Logger.Info("Check if password is needed (unsupported currently)...");
                    OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = "";

                    Dictionary<string, string> postValues = new Dictionary<string, string>
                    {
                        { "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
                        { "page_token", string.Empty },
                        { "page_index", "0" },
                        { "q", "" }
                    };
                    HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
                    HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);

                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

                        BhadooIndexResponse response = BhadooIndexResponse.FromJson(responseJson);

                        webDirectory = await ScanAsync(httpClient, webDirectory);
                    }
                }
                else
                {
                    webDirectory = await ScanAsync(httpClient, webDirectory);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error parsing {Parser} for URL: {webDirectory.Url}");
                webDirectory.Error = true;

                OpenDirectoryIndexer.Session.Errors++;

                if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
                {
                    OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
                }

                throw;
            }

            return webDirectory;
        }

        private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
        {
            Logger.Debug($"Retrieving listings for {webDirectory.Uri} with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");

            webDirectory.Parser = Parser;

            try
            {
                if (!webDirectory.Url.EndsWith("/"))
                {
                    webDirectory.Url += "/";
                }

                long pageIndex = 0;
                string nextPageToken = string.Empty;

                do
                {
                    Logger.Warn($"Retrieving listings for {webDirectory.Uri} with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}, page {pageIndex + 1}");

                    Dictionary<string, string> postValues = new Dictionary<string, string>
                    {
                        { "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
                        { "page_token", nextPageToken },
                        { "page_index", pageIndex.ToString() }
                    };
                    HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
                    HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);

                    webDirectory.ParsedSuccesfully = httpResponseMessage.IsSuccessStatusCode;
                    httpResponseMessage.EnsureSuccessStatusCode();

                    string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

                    BhadooIndexResponse indexResponse = BhadooIndexResponse.FromJson(responseJson);

                    nextPageToken = indexResponse.NextPageToken;
                    pageIndex = indexResponse.CurPageIndex + 1;

                    foreach (File file in indexResponse.Data.Files)
                    {
                        if (file.MimeType == FolderMimeType)
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = Parser,
                                // Yes, string concatenation, do not use new Uri(webDirectory.Uri, file.Name), because things could end with a space...
                                Url = $"{webDirectory.Uri}{file.Name}/",
                                Name = file.Name
                            });
                        }
                        else
                        {
                            webDirectory.Files.Add(new WebFile
                            {
                                Url = new Uri(webDirectory.Uri, file.Name).ToString(),
                                FileName = file.Name,
                                FileSize = file.Size
                            });
                        }
                    }
                } while (!string.IsNullOrWhiteSpace(nextPageToken));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error processing {Parser} for URL: {webDirectory.Url}");
                webDirectory.Error = true;

                OpenDirectoryIndexer.Session.Errors++;

                if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
                {
                    OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
                }

                //throw;
            }

            return webDirectory;
        }
    }
}
