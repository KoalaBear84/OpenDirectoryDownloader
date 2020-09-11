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
        private static readonly Random Jitterer = new Random();
        private static readonly AsyncRetryPolicy RetryPolicyNew = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(100,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, retryAttempt))) + TimeSpan.FromMilliseconds(Jitterer.Next(0, 200)),
                onRetry: (ex, span, retryCount, context) =>
                {
                    WebDirectory webDirectory = context["WebDirectory"] as WebDirectory;

                    string relativeUrl = webDirectory.Uri.PathAndQuery;

                    if (ex is FtpAuthenticationException ftpAuthenticationException)
                    {
                        Logger.Error($"[{context["Processor"]}] Error {ftpAuthenticationException.CompletionCode} {ftpAuthenticationException.Message} retrieving on try {retryCount} for url '{relativeUrl}'. Stopping.");

                        if (ftpAuthenticationException.ResponseType == FtpResponseType.PermanentNegativeCompletion)
                        {
                            (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                            return;
                        }
                    }

                    if (retryCount <= 4)
                    {
                        Logger.Warn($"[{context["Processor"]}] Error {ex.Message} retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {span.TotalSeconds:F0} seconds.");
                    }
                    else
                    {
                        (context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
                    }
                }
            );

        public static Dictionary<string, FtpClient> FtpClients { get; set; } = new Dictionary<string, FtpClient>();

        public static async Task<WebDirectory> ParseFtpAsync(string processor, WebDirectory webDirectory, string username, string password)
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

            Context pollyContext = new Context
            {
                { "Processor", processor },
                { "WebDirectory", webDirectory },
                { "CancellationTokenSource", cancellationTokenSource }
            };

            return (await RetryPolicyNew.ExecuteAndCaptureAsync(async (context, token) => { return await ParseFtpInnerAsync(processor, webDirectory, username, password, cancellationTokenSource.Token); }, pollyContext, cancellationTokenSource.Token)).Result;
        }

        private static async Task<WebDirectory> ParseFtpInnerAsync(string processor, WebDirectory webDirectory, string username, string password, CancellationToken cancellationToken)
        {
            if (!FtpClients.ContainsKey(processor))
            {
                if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
                {
                    GetCredentials(webDirectory, out string username1, out string password1);

                    username = username1;
                    password = password1;
                }

                FtpClients[processor] = new FtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port, username, password)
                {
                    ConnectTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    DataConnectionConnectTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    DataConnectionReadTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    ReadTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
                    EncryptionMode = OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_FtpEncryptionMode) ? Enum.Parse<FtpEncryptionMode>(OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_FtpEncryptionMode]) : FtpEncryptionMode.None
                };

                FtpClients[processor].ValidateAnyCertificate = true;

                try
                {
                await FtpClients[processor].ConnectAsync(cancellationToken);

                if (!FtpClients[processor].IsConnected)
                {
                    FtpClients.Remove(processor);
                    throw new Exception("Error connecting to FTP");
                }
            }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error connecting to FTP");
                    throw ex;
                }
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

        public static async Task<string> GetFtpServerInfo(WebDirectory webDirectory, string username, string password)
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

            string processor = "Initalize";

            Context pollyContext = new Context
            {
                { "Processor", processor },
                { "WebDirectory", webDirectory },
                { "CancellationTokenSource", cancellationTokenSource }
            };

            return (await RetryPolicyNew.ExecuteAndCaptureAsync(async (context, token) => { return await GetFtpServerInfoInnerAsync(webDirectory, username, password, cancellationTokenSource.Token); }, pollyContext, cancellationTokenSource.Token)).Result;
        }

        private static async Task<string> GetFtpServerInfoInnerAsync(WebDirectory webDirectory, string username, string password, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
            {
                GetCredentials(webDirectory, out string username1, out string password1);

                username = username1;
                password = password1;
        }

            // Try multiple possible options, the AutoDetect and AutoConnectAsync are not working (reliably)
            foreach (FtpEncryptionMode ftpEncryptionMode in Enum.GetValues(typeof(FtpEncryptionMode)))
            {
                try
                {
                    Logger.Warn($"Try FTP(S) connection with EncryptionMode {ftpEncryptionMode}");

                    FtpClient ftpClient = new FtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port, username, password)
        {
                        EncryptionMode = ftpEncryptionMode
                    };

                    ftpClient.ValidateAnyCertificate = true;
                    await ftpClient.ConnectAsync(cancellationToken);

                    OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_FtpEncryptionMode] = ftpEncryptionMode.ToString();

            FtpReply connectReply = ftpClient.LastReply;

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
                catch (Exception ex)
                {
                    Logger.Error($"FTP EncryptionMode {ftpEncryptionMode} failed: {ex.Message}");
                }
            }

            return null;
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
