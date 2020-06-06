using FluentFTP;
using NLog;
using OpenDirectoryDownloader.Shared.Models;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    public class FtpParser
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(4,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, span, retryCount, context) =>
                {
                    WebDirectory webDirectory = context["WebDirectory"] as WebDirectory;
                    Logger.Warn($"[{context["Processor"]}] Error {ex.Message} retrieving on try {retryCount} for url '{webDirectory.Url}'. Waiting {span.TotalSeconds} seconds.");
                }
            );

        public static Dictionary<string, FtpClient> FtpClients { get; set; } = new Dictionary<string, FtpClient>();

        public static async Task<WebDirectory> ParseFtpAsync(string processor, WebDirectory webDirectory)
        {
            Context pollyContext = new Context
            {
                { "Processor", string.Empty },
                { "WebDirectory", webDirectory }
            };

            return (await RetryPolicy.ExecuteAndCaptureAsync(ctx => ParseFtpInnerAsync(processor, webDirectory), pollyContext)).Result;
        }

        private static async Task<WebDirectory> ParseFtpInnerAsync(string processor, WebDirectory webDirectory)
        {
            if (!FtpClients.ContainsKey(processor))
            {
                GetCredentials(webDirectory, out string username, out string password);

                FtpClients[processor] = new FtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port, username, password)
                {
                    ConnectTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    DataConnectionConnectTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    DataConnectionReadTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    ReadTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds
                };

                await FtpClients[processor].ConnectAsync();
            }

            // TODO: If anybody knows a better way.. PR!
            string relativeUrl = webDirectory.Uri.LocalPath + WebUtility.UrlDecode(webDirectory.Uri.Fragment);

            Logger.Debug($"Started retrieving {relativeUrl}...");

            foreach (FtpListItem item in FtpClients[processor].GetListing(relativeUrl))
            {
                // Some strange FTP servers.. Give parent directoryies back..
                if (item.Name == "/" || item.FullName == webDirectory.Uri.LocalPath || !item.FullName.StartsWith(webDirectory.Uri.LocalPath))
                {
                    continue;
                }

                Uri uri = new Uri(new Uri(webDirectory.Url), item.FullName);
                string fullUrl = uri.ToString();

                if (item.Type == FtpFileSystemObjectType.File)
                {
                    webDirectory.Files.Add(new WebFile
                    {
                        Url = fullUrl,
                        FileName = Path.GetFileName(new Uri(fullUrl).LocalPath),
                        FileSize = item.Size
                    });
                }

                if (item.Type == FtpFileSystemObjectType.Directory)
                {
                    if (webDirectory.Url != fullUrl)
                    {
                        webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                        {
                            Url = fullUrl,
                            Name = item.Name
                        });
                    }
                }
            }

            Logger.Debug($"Finished retrieving {relativeUrl}");

            return webDirectory;
        }

        public static async Task<string> GetFtpServerInfo(WebDirectory webDirectory)
        {
            Context pollyContext = new Context
            {
                { "Processor", string.Empty },
                { "WebDirectory", webDirectory }
            };

            return (await RetryPolicy.ExecuteAndCaptureAsync(ctx => GetFtpServerInfoInnerAsync(webDirectory), pollyContext)).Result;
        }

        private static async Task<string> GetFtpServerInfoInnerAsync(WebDirectory webDirectory)
        {
            GetCredentials(webDirectory, out string username, out string password);

            FtpClient ftpClient = new FtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port, username, password);

            await ftpClient.ConnectAsync();

            FtpReply connectReply = ftpClient.LastReply;
            CancellationToken cancellationToken = new CancellationToken();

            FtpReply helpReply = await ftpClient.ExecuteAsync("HELP", cancellationToken);
            FtpReply statusReply = await ftpClient.ExecuteAsync("STAT", cancellationToken);
            FtpReply systemReply = await ftpClient.ExecuteAsync("SYST", cancellationToken);

            return
                $"Connect Respones: {connectReply.InfoMessages}{Environment.NewLine}" +
                $"ServerType: {ftpClient.ServerType}{Environment.NewLine}" +
                $"Help response: {helpReply.InfoMessages}{Environment.NewLine}" +
                $"Status response: {statusReply.InfoMessages}{Environment.NewLine}" +
                $"System response: {systemReply.InfoMessages}{Environment.NewLine}";
        }

        public static async void CloseAll()
        {
            foreach (KeyValuePair<string, FtpClient> keyValuePair in FtpClients)
            {
                await keyValuePair.Value.DisconnectAsync();
            }
        }

        private static void GetCredentials(WebDirectory webDirectory, out string username, out string password)
        {
            username = "anonymous";
            password = "password";

            if (webDirectory.Uri.UserInfo?.Contains(':') == true)
            {
                string[] splitted = webDirectory.Uri.UserInfo.Split(':');

                username = WebUtility.UrlDecode(splitted.First());
                password = WebUtility.UrlDecode(splitted.Last());
            }
        }
    }
}
