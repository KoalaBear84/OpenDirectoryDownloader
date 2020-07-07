using NLog;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.BlitzfilesTech
{
    /// <summary>
    /// Similar to GoIndex
    /// </summary>
    public static class BlitzfilesTechParser
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Regex DriveHashRegex = new Regex(@"\/drive\/s\/(?<DriveHash>.*)");
        const string Parser = "BlitzfilesTech";

        public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
        {
            try
            {
                string driveHash = GetDriveHash(webDirectory);

                if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
                {
                    Console.WriteLine($"{Parser} will always be indexed with only 1 thread, else you will run into problems and errors.");
                    Logger.Info($"{Parser} will always be indexed with only 1 thread, else you will run into problems and errors.");
                    OpenDirectoryIndexer.Session.MaxThreads = 1;

                    //Console.WriteLine("Check if password is needed (unsupported currently)...");
                    //Logger.Info("Check if password is needed (unsupported currently)...");
                    //OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = string.Empty;

                    HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(GetFolderUrl(driveHash, string.Empty, 0));

                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

                        BlitzfilesTechResponse response = BlitzfilesTechResponse.FromJson(responseJson);

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

        private static string GetDriveHash(WebDirectory webDirectory)
        {
            Match driveHashRegexMatch = DriveHashRegex.Match(webDirectory.Url);

            if (!driveHashRegexMatch.Success)
            {
                throw new Exception("Error getting drivehash");
            }

            string driveHash = driveHashRegexMatch.Groups["DriveHash"].Value;
            return driveHash;
        }

        private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
        {
            Logger.Debug($"Retrieving listings for {webDirectory.Uri} with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");

            webDirectory.Parser = Parser;

            try
            {
                string driveHash = GetDriveHash(webDirectory);
                string entryHash = string.Empty;
                long pageIndex = 0;
                long totalPages = 0;

                do
                {
                    Logger.Warn($"Retrieving listings for {webDirectory.Uri} with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}, page {pageIndex + 1}");

                    HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(GetFolderUrl(driveHash, entryHash, pageIndex));

                    webDirectory.ParsedSuccesfully = httpResponseMessage.IsSuccessStatusCode;
                    httpResponseMessage.EnsureSuccessStatusCode();

                    string responseJson = await httpResponseMessage.Content.ReadAsStringAsync();

                    BlitzfilesTechResponse indexResponse = BlitzfilesTechResponse.FromJson(responseJson);
                    entryHash = indexResponse.Link.Entry.Hash;

                    pageIndex = indexResponse.FolderChildren.CurrentPage;
                    totalPages = indexResponse.FolderChildren.LastPage;

                    foreach (Entry entry in indexResponse.FolderChildren.Data)
                    {
                        if (entry.Type == "folder")
                        {
                            webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                            {
                                Parser = Parser,
                                Url = $"https://blitzfiles.tech/files/drive/s/{driveHash}:{entry.Hash}",
                                Name = entry.Name
                            });
                        }
                        else
                        {
                            webDirectory.Files.Add(new WebFile
                            {
                                Url = $"https://blitzfiles.tech/files/secure/uploads/download?hashes={entry.Hash}&shareable_link={indexResponse.Link.Id}",
                                FileName = entry.Name,
                                FileSize = entry.FileSize
                            });
                        }
                    }
                } while (pageIndex < totalPages);
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

        private static string GetFolderUrl(string driveHash, string entryHash, long pageIndex)
        {
            return $"https://blitzfiles.tech/files/secure/drive/shareable-links/{driveHash}{(!string.IsNullOrWhiteSpace(entryHash) ? $":{entryHash}" : string.Empty)}?page={pageIndex + 1}&order=updated_at:desc&withEntries=true";
        }
    }
}
