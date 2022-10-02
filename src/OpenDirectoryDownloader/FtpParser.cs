using FluentFTP;
using OpenDirectoryDownloader.Shared.Models;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader;

public class FtpParser
{
	private static readonly Regex RegexMaxThreadsSpecific01 = new(@"Too many connections \((?<MaxThreads>\d*)\) from this IP");
	private static readonly Regex RegexMaxThreadsSpecific02 = new(@"Sorry, the maximum number of clients \((?<MaxThreads>\d*)\) from your host are already connected.");
	private static readonly Regex RegexMaxThreadsSpecific03 = new(@"Sorry, your system may not connect more than (?<MaxThreads>\d*) times.");
	private static readonly Regex RegexMaxThreadsSpecific04 = new(@"Not logged in, only (?<MaxThreads>\d*) sessions from same IP allowed concurrently.");

	private static readonly Regex RegexMaxThreadsGeneral01 = new(@"Too many connections");
	private static readonly Regex RegexMaxThreadsGeneral02 = new(@"There are too many connections from your internet address.");
	private static readonly Regex RegexMaxThreadsGeneral03 = new(@"No more connections allowed from your IP.");
	private static readonly Regex RegexMaxThreadsGeneral04 = new(@"There are too many connected users, please try later.");
	private static readonly Regex RegexMaxThreadsGeneral05 = new(@"Too many users logged in for this account.*");
	private static readonly Regex RegexMaxThreadsGeneral06 = new(@"Sorry, the maximum number of clients \(\d*\) for this user are already connected.");

	private static readonly Random Jitterer = new();
	private static readonly AsyncRetryPolicy RetryPolicyNew = Policy
		.Handle<Exception>()
		.WaitAndRetryAsync(100,
			sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, retryAttempt))) + TimeSpan.FromMilliseconds(Jitterer.Next(0, 200)),
			onRetry: (ex, span, retryCount, context) =>
			{
				WebDirectory webDirectory = context["WebDirectory"] as WebDirectory;

				string relativeUrl = webDirectory.Uri.PathAndQuery;

				if (ex is FtpCommandException ftpCommandException)
				{
					if (IsMaxThreads(ftpCommandException))
					{
						Program.Logger.Warning("[{thread}] Maximum connections reached: {message}", context["Thread"], ftpCommandException.Message);
						// Stop this thread nicely
						webDirectory.CancellationReason = Constants.Ftp_Max_Connections;
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
						return;
					}
				}

				if (ex is FtpAuthenticationException ftpAuthenticationException)
				{
					Program.Logger.Error("[{thread}] Error {completionCode} {message} retrieving on try {retryCount} for '{relativeUrl}'. Stopping.", context["Thread"], ftpAuthenticationException.CompletionCode, ftpAuthenticationException.Message, retryCount, relativeUrl);

					if (ftpAuthenticationException.ResponseType == FtpResponseType.PermanentNegativeCompletion)
					{
						(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
						return;
					}
				}

				if (retryCount <= 4)
				{
					Program.Logger.Warning("[{thread}] Error {error} retrieving on try {retryCount} for '{relativeUrl}'. Waiting {waitTime:F0} seconds.", context["Thread"], ex.Message, retryCount, relativeUrl, span.TotalSeconds);
				}
				else
				{
					(context["CancellationTokenSource"] as CancellationTokenSource).Cancel();
				}
			}
		);

	private static bool IsMaxThreads(FtpCommandException ftpCommandException)
	{
		List<Regex> regexes = new()
		{
			RegexMaxThreadsSpecific01,
			RegexMaxThreadsSpecific02,
			RegexMaxThreadsSpecific03,
			RegexMaxThreadsSpecific04,

            // General needs to be check the latest
            RegexMaxThreadsGeneral01,
			RegexMaxThreadsGeneral02,
			RegexMaxThreadsGeneral03,
			RegexMaxThreadsGeneral05,
			RegexMaxThreadsGeneral06
		};

		foreach (Regex regex in regexes)
		{
			if (regex.IsMatch(ftpCommandException.Message))
			{
				Match regexMatch = regex.Match(ftpCommandException.Message);

				int newThreads = OpenDirectoryIndexer.Session.MaxThreads;

				if (regexMatch.Groups.ContainsKey("MaxThreads"))
				{
					// Should be one less than..
					if (regex == RegexMaxThreadsSpecific01)
					{
						OpenDirectoryIndexer.Session.MaxThreads = Math.Max(1, int.Parse(regexMatch.Groups["MaxThreads"].Value) - 1);
					}
					else
					{
						OpenDirectoryIndexer.Session.MaxThreads = Math.Max(1, int.Parse(regexMatch.Groups["MaxThreads"].Value));
					}
				}
				else
				{
					OpenDirectoryIndexer.Session.MaxThreads = Math.Max(1, Interlocked.Decrement(ref OpenDirectoryIndexer.Session.MaxThreads));
				}

				if (newThreads != OpenDirectoryIndexer.Session.MaxThreads)
				{
					Program.Logger.Warning("Max threads reduced to {maxThreads}", OpenDirectoryIndexer.Session.MaxThreads);
				}

				return true;
			}
		}

		// This one is not specific to threads
		if (RegexMaxThreadsGeneral04.IsMatch(ftpCommandException.Message))
		{
			throw ftpCommandException;
		}

		return false;
	}

	public static Dictionary<string, AsyncFtpClient> FtpClients { get; set; } = new Dictionary<string, AsyncFtpClient>();

	public static async Task<WebDirectory> ParseFtpAsync(string threadName, WebDirectory webDirectory, string username, string password)
	{
		CancellationTokenSource cancellationTokenSource = new();

		cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

		Context pollyContext = new()
		{
			{ "Thread", threadName },
			{ "WebDirectory", webDirectory },
			{ "CancellationTokenSource", cancellationTokenSource }
		};

		return (await RetryPolicyNew.ExecuteAndCaptureAsync(async (context, token) => { return await ParseFtpInnerAsync(threadName, webDirectory, username, password, cancellationTokenSource.Token); }, pollyContext, cancellationTokenSource.Token)).Result;
	}

	private static async Task<WebDirectory> ParseFtpInnerAsync(string threadName, WebDirectory webDirectory, string username, string password, CancellationToken cancellationToken)
	{
		if (!FtpClients.ContainsKey(threadName))
		{
			if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
			{
				GetCredentials(webDirectory, out string username1, out string password1);

				username = username1;
				password = password1;
			}

			Program.Logger.Warning("[{thread}] Connecting to FTP...", threadName);

			int timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;

			FtpClients[threadName] = new AsyncFtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port)
			{
				Credentials = new NetworkCredential(username, password),
				Config = new FtpConfig
				{
					ConnectTimeout = timeout,
					DataConnectionConnectTimeout = timeout,
					DataConnectionReadTimeout = timeout,
					ReadTimeout = timeout * 2,
					EncryptionMode = OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_FtpEncryptionMode) ? Enum.Parse<FtpEncryptionMode>(OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_FtpEncryptionMode]) : FtpEncryptionMode.None,
					ValidateAnyCertificate = true
				}
			};

			try
			{
				await FtpClients[threadName].Connect(cancellationToken);

				if (!FtpClients[threadName].IsConnected)
				{
					FtpClients.Remove(threadName);
					throw new Exception($"[{threadName}] Error connecting to FTP");
				}
			}
			catch (Exception ex)
			{
				Program.Logger.Error(ex, "[{thread}] Error connecting to FTP", threadName);
				throw;
			}
		}

		// TODO: If anybody knows a better way.. PR!
		string relativeUrl = webDirectory.Uri.LocalPath + WebUtility.UrlDecode(webDirectory.Uri.Fragment);

		Program.Logger.Debug("Started retrieving {url}..", relativeUrl);

		foreach (FtpListItem item in await FtpClients[threadName].GetListing(relativeUrl, cancellationToken))
		{
			// Some strange FTP servers.. Give parent directoryies back..
			if (item.Name == "/" || item.FullName == webDirectory.Uri.LocalPath || !item.FullName.StartsWith(webDirectory.Uri.LocalPath))
			{
				continue;
			}

			Uri uri = new(new Uri(webDirectory.Url), item.FullName);
			string fullUrl = uri.ToString();

			if (item.Type == FtpObjectType.File)
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(new Uri(fullUrl).LocalPath),
					FileSize = item.Size
				});
			}
			else if (item.Type == FtpObjectType.Directory)
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

		Program.Logger.Debug("Finished retrieving {relativeUrl}", relativeUrl);

		return webDirectory;
	}

	public static async Task<string> GetFtpServerInfo(WebDirectory webDirectory, string username, string password)
	{
		CancellationTokenSource cancellationTokenSource = new();

		cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

		string threadName = "Initalize";

		Context pollyContext = new()
		{
			{ "Thread", threadName },
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
				Program.Logger.Warning("Try FTP(S) connection with EncryptionMode {ftpEncryptionMode}", ftpEncryptionMode);

				int timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds; 
				
				AsyncFtpClient ftpClient = new AsyncFtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port)
				{
					Credentials = new NetworkCredential(username, password),
					Config = new FtpConfig
					{
						ConnectTimeout = timeout,
						DataConnectionConnectTimeout = timeout,
						DataConnectionReadTimeout = timeout,
						ReadTimeout = timeout * 2,
						EncryptionMode = ftpEncryptionMode,
						ValidateAnyCertificate = true
					}
				}; 
				
				await ftpClient.Connect(cancellationToken);

				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_FtpEncryptionMode] = ftpEncryptionMode.ToString();

				FtpReply connectReply = ftpClient.LastReply;

				FtpReply helpReply = await ftpClient.Execute("HELP", cancellationToken);
				FtpReply statusReply = await ftpClient.Execute("STAT", cancellationToken);
				FtpReply systemReply = await ftpClient.Execute("SYST", cancellationToken);

				await ftpClient.Disconnect(cancellationToken);

				return
					$"Connect Respones: {connectReply.InfoMessages}{Environment.NewLine}" +
					$"ServerType: {ftpClient.ServerType}{Environment.NewLine}" +
					$"Help response: {helpReply.InfoMessages}{Environment.NewLine}" +
					$"Status response: {statusReply.InfoMessages}{Environment.NewLine}" +
					$"System response: {systemReply.InfoMessages}{Environment.NewLine}";
			}
			catch (Exception ex)
			{
				if (ex is FtpCommandException ftpCommandException)
				{
					if (IsMaxThreads(ftpCommandException))
					{
						return null;
					}
				}

				Program.Logger.Error("FTP EncryptionMode {ftpEncryptionMode} failed: {error}", ftpEncryptionMode, ex.Message);
			}
		}

		return null;
	}

	public static async void CloseAll(AsyncFtpClient exceptFtpClient = null)
	{
		foreach (KeyValuePair<string, AsyncFtpClient> keyValuePair in FtpClients)
		{
			AsyncFtpClient ftpClient = keyValuePair.Value;

			if (exceptFtpClient is null || ftpClient != exceptFtpClient)
			{
				await ftpClient.Disconnect();
			}
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
