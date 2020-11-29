using FluentFTP;
using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    public class Library
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string GetApplicationPath()
        {
            string appPath = AppContext.BaseDirectory;

            if (!appPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                appPath += Path.DirectorySeparatorChar;
            }

            return appPath;
        }

        public static string GetScansPath()
        {
            string scansPath = $"{GetApplicationPath()}Scans";

            if (!Directory.Exists(scansPath))
            {
                Directory.CreateDirectory(scansPath);
            }

            return scansPath;
        }

        public static bool IsBase64String(string base64)
        {
            Span<byte> buffer = new Span<byte>(new byte[base64.Length]);
            return Convert.TryFromBase64String(base64, buffer, out _);
        }

        public static string FixUrl(string url)
        {
            url = url.Trim();

            if (IsBase64String(url))
            {
                byte[] data = Convert.FromBase64String(url);
                url = Encoding.UTF8.GetString(data);
            }

            if (!url.Contains("http:") && !url.Contains("https:") && !url.Contains("ftp:") && !url.Contains("ftps:"))
            {
                url = $"http://{url}";
            }

            Uri uri = new Uri(url);

            if (!url.EndsWith("/") && string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(uri.AbsolutePath))) && string.IsNullOrWhiteSpace(uri.Query))
            {
                url += "/";
            }

            return url;
        }

        public static void SaveSessionJson(Session session)
        {
            JsonSerializer jsonSerializer = new JsonSerializer();

            string scansPath = GetScansPath();

            using (StreamWriter streamWriter = new StreamWriter(Path.Combine(scansPath, $"{CleanUriToFilename(session.Root.Uri)}.json")))
            {
                using (JsonWriter jsonWriter = new JsonTextWriter(streamWriter))
                {
                    jsonSerializer.Serialize(jsonWriter, session);
                }
            }
        }

        public static string CleanUriToFilename(Uri uri)
        {
            return PathHelper.GetValidPath(WebUtility.UrlDecode(uri.ToString()));
        }

        public static Session LoadSessionJson(string fileName)
        {
            using (StreamReader streamReader = new StreamReader(fileName))
            {
                using (JsonReader jsonReader = new JsonTextReader(streamReader))
                {
                    return new JsonSerializer().Deserialize<Session>(jsonReader);
                }
            }
        }

        public static string FormatWithThousands(object value)
        {
            return string.Format("{0:#,0}", value);
        }

        private static double GetSpeedInMBs(IGrouping<long, KeyValuePair<long, long>> measurements, int useMiliseconds = 0)
        {
            long time = useMiliseconds == 0 ? measurements.Last().Key - measurements.First().Key : useMiliseconds;
            double downloadedMBs = (measurements.Last().Value - measurements.First().Value) / 1024 / 1024d;
            return downloadedMBs / (time / 1000d);
        }

        public static async Task<SpeedtestResult> DoSpeedTestHttpAsync(HttpClient httpClient, string url, int seconds = 25)
        {
            Logger.Info($"Do HTTP speedtest for {url}");

            HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.RequestMessage.RequestUri.ToString() != url)
            {
                httpClient.DefaultRequestHeaders.Referrer = GetUrlDirectory(url);
                httpResponseMessage.Dispose();
                httpResponseMessage = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            }

            if (!httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.RequestMessage.RequestUri.ToString() != url)
            {
                string retrievedUrl = null;

                if (httpResponseMessage.RequestMessage.RequestUri.ToString() != url)
                {
                    retrievedUrl = httpResponseMessage.RequestMessage.RequestUri.ToString();
                }
                else if (httpResponseMessage.Headers.Location is not null)
                {
                    retrievedUrl = httpResponseMessage.Headers.Location.ToString();
                }

                Logger.Warn($"Speedtest cancelled because it returns HTTP {(int)httpResponseMessage.StatusCode}{(retrievedUrl is not null ? $" with URL {retrievedUrl}" : string.Empty)}");
                return new SpeedtestResult();
            }

            try
            {
                using (Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync())
                {
                    SpeedtestResult speedtestResult = SpeedtestFromStream(stream, seconds);

                    return speedtestResult;
                }
            }
            finally
            {
                httpResponseMessage.Dispose();
            }
        }

        public static async Task<SpeedtestResult> DoSpeedTestFtpAsync(FtpClient ftpClient, string url, int seconds = 25)
        {
            Logger.Info($"Do FTP speedtest for {url}");

            Uri uri = new Uri(url);

            using (Stream stream = await ftpClient.OpenReadAsync(uri.LocalPath))
            {
                SpeedtestResult speedtestResult = SpeedtestFromStream(stream, seconds);

                return speedtestResult;
            }
        }

        private static SpeedtestResult SpeedtestFromStream(Stream stream, int seconds)
        {
            int miliseconds = seconds * 1000;

            Stopwatch stopwatch = Stopwatch.StartNew();
            long totalBytesRead = 0;

            byte[] buffer = new byte[2048];
            int bytesRead;

            List<KeyValuePair<long, long>> measurements = new List<KeyValuePair<long, long>>(10_000);
            long previousTime = 0;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (stopwatch.ElapsedMilliseconds >= miliseconds)
                {
                    break;
                }

                if (previousTime / 1000 < stopwatch.ElapsedMilliseconds / 1000)
                {
                    ClearCurrentLine();
                    double maxMBsPerSecond = measurements.Any() ? measurements.GroupBy(m => m.Key / 1000).Max(s => GetSpeedInMBs(s, 1000)) : 0;
                    Console.Write($"Downloaded: {FileSizeHelper.ToHumanReadable(totalBytesRead)}, Time: {stopwatch.ElapsedMilliseconds / 1000}s, Speed: {maxMBsPerSecond:F1} MB/s ({maxMBsPerSecond * 8:F0} mbit)");
                }

                if (stopwatch.ElapsedMilliseconds >= 10_000)
                {
                    // Second changed
                    if (previousTime / 1000 < stopwatch.ElapsedMilliseconds / 1000)
                    {
                        List<IGrouping<long, KeyValuePair<long, long>>> perSecond = measurements.GroupBy(m => m.Key / 1000).ToList();

                        if (!perSecond.Any())
                        {
                            break;
                        }

                        double maxSpeedLastSeconds = perSecond.TakeLast(3).Max(s => GetSpeedInMBs(s, 1000));
                        double maxSpeedBefore = perSecond.Take(perSecond.Count - 3).Max(s => GetSpeedInMBs(s, 1000));

                        // If no improvement in speed
                        if (maxSpeedBefore > maxSpeedLastSeconds)
                        {
                            break;
                        }
                    }
                }

                totalBytesRead += bytesRead;

                measurements.Add(new KeyValuePair<long, long>(stopwatch.ElapsedMilliseconds, totalBytesRead));
                previousTime = stopwatch.ElapsedMilliseconds;
            }

            Console.WriteLine();

            stopwatch.Stop();

            SpeedtestResult speedtestResult = new SpeedtestResult
            {
                DownloadedBytes = totalBytesRead,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                MaxMBsPerSecond = measurements.Any() ? measurements.GroupBy(m => m.Key / 1000).Max(s => GetSpeedInMBs(s, 1000)) : 0
            };

            if (measurements.Any())
            {
                Logger.Info($"Downloaded: {speedtestResult.DownloadedMBs:F2} MB, Time: {speedtestResult.ElapsedMilliseconds} ms, Speed: {speedtestResult.MaxMBsPerSecond:F1} MB/s ({speedtestResult.MaxMBsPerSecond * 8:F0} mbit)");
            }
            else
            {
                Logger.Warn($"Speedtest failed, nothing downloaded.");
            }

            return speedtestResult;
        }

        private static void ClearCurrentLine()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    Console.Write(new string('+', Console.WindowWidth).Replace("+", "\b \b"));
                }
                else
                {
                    Console.WriteLine();
                }
            }
            catch
            {
                // Happens when console is redirected, and just to be sure
                Console.WriteLine();
            }
        }

        private static Uri GetUrlDirectory(string url)
        {
            return new Uri(new Uri(url), ".");
        }

        public static DateTime UnixTimestampToDateTime(long unixTimeStamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixTimeStamp);
        }

        public static Stream GetEmbeddedResourceStream(Assembly assembly, string resourceFileName)
        {
            List<string> resourcePaths = assembly.GetManifestResourceNames().Where(x => x.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (resourcePaths.Count == 1)
            {
                return assembly.GetManifestResourceStream(resourcePaths.Single());
            }

            return null;
        }
    }
}
