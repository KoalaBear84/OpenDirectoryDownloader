using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Threading;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Protocols.Ftp;

/// <summary>Lists FTP(S) directories, one connection per crawl worker. Servers limiting connections lower the scan's concurrency.</summary>
public sealed partial class FtpDirectoryFetcher(ScanSession session, Credentials credentials, ILogger<FtpDirectoryFetcher> logger) : IAsyncDisposable
{
	private const int MaxAttempts = 5;

	private static readonly TimeSpan DirectoryTimeLimit = TimeSpan.FromMinutes(5);

	private readonly ConcurrentDictionary<string, AsyncFtpClient> _clients = new();

	/// <summary>The encryption mode that worked when the server info was read</summary>
	private FtpEncryptionMode _encryptionMode = FtpEncryptionMode.None;

	public enum Outcome
	{
		Listed,
		Failed,

		/// <summary>The server has too many connections: put the directory back for another worker</summary>
		MaxConnections
	}

	/// <summary>A connected client, for the speedtest</summary>
	public AsyncFtpClient AnyConnectedClient => _clients.Values.FirstOrDefault(c => c.IsConnected);

	/// <summary>Lists <paramref name="webDirectory"/> into a new, detached directory.</summary>
	public async Task<(Outcome Outcome, WebDirectory Result)> FetchAsync(string workerName, WebDirectory webDirectory, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeLimit.CancelAfter(DirectoryTimeLimit);

		string relativeUrl = webDirectory.Uri.PathAndQuery;

		for (int attempt = 1; ; attempt++)
		{
			try
			{
				return (Outcome.Listed, await ListAsync(workerName, webDirectory, timeLimit.Token));
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (OperationCanceledException)
			{
				logger.LogWarning("[{thread}] Time limit reached for '{relativeUrl}'. Stopping.", workerName, relativeUrl);
				return (Outcome.Failed, null);
			}
			catch (Exception ex)
			{
				if (ex is FtpCommandException ftpCommandException && IsMaxThreads(ftpCommandException))
				{
					logger.LogWarning("[{thread}] Maximum connections reached: {message}", workerName, ftpCommandException.Message);
					return (Outcome.MaxConnections, null);
				}

				if (ex is FtpAuthenticationException ftpAuthenticationException)
				{
					logger.LogError("[{thread}] Error {completionCode} {message} retrieving on try {retryCount} for '{relativeUrl}'. Stopping.", workerName, ftpAuthenticationException.CompletionCode, ftpAuthenticationException.Message, attempt, relativeUrl);

					if (ftpAuthenticationException.ResponseType == FtpResponseType.PermanentNegativeCompletion)
					{
						return (Outcome.Failed, null);
					}
				}

				if (attempt >= MaxAttempts)
				{
					return (Outcome.Failed, null);
				}

				TimeSpan wait = TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, attempt))) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200));
				logger.LogWarning("[{thread}] Error {error} retrieving on try {retryCount} for '{relativeUrl}'. Waiting {waitTime:F0} seconds.", workerName, ex.Message, attempt, relativeUrl, wait.TotalSeconds);

				try
				{
					await Task.Delay(wait, timeLimit.Token);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					return (Outcome.Failed, null);
				}
			}
		}
	}

	private async Task<WebDirectory> ListAsync(string workerName, WebDirectory webDirectory, CancellationToken cancellationToken)
	{
		if (!_clients.TryGetValue(workerName, out AsyncFtpClient client))
		{
			GetCredentials(webDirectory, out string username, out string password);

			logger.LogWarning("[{thread}] Connecting to FTP...", workerName);

			client = CreateClient(webDirectory, username, password, _encryptionMode);

			try
			{
				await client.Connect(cancellationToken);

				if (!client.IsConnected)
				{
					throw new Exception($"[{workerName}] Error connecting to FTP");
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "[{thread}] Error connecting to FTP", workerName);
				await client.DisposeAsync();
				throw;
			}

			_clients[workerName] = client;
		}

		WebDirectory result = new(webDirectory.ParentDirectory)
		{
			Url = webDirectory.Url,
			Name = webDirectory.Name
		};

		// TODO: If anybody knows a better way.. PR!
		string relativeUrl = webDirectory.Uri.LocalPath + WebUtility.UrlDecode(webDirectory.Uri.Fragment);

		logger.LogDebug("Started retrieving {url}..", relativeUrl);

		foreach (FtpListItem item in await client.GetListing(relativeUrl, cancellationToken))
		{
			// Some strange FTP servers.. Give parent directories back..
			if (item.Name == "/" || item.FullName == webDirectory.Uri.LocalPath || !item.FullName.StartsWith(webDirectory.Uri.LocalPath))
			{
				continue;
			}

			Uri uri = new(webDirectory.Uri, item.FullName);
			string fullUrl = uri.ToString();

			switch (item.Type)
			{
				case FtpObjectType.File:
					result.Files.Add(new WebFile
					{
						Url = fullUrl,
						FileName = Path.GetFileName(new Uri(fullUrl).LocalPath),
						FileSize = item.Size
					});
					break;
				case FtpObjectType.Directory:
					if (webDirectory.Url != fullUrl)
					{
						result.Subdirectories.Add(new WebDirectory(result)
						{
							Url = fullUrl,
							Name = item.Name
						});
					}

					break;
			}
		}

		logger.LogDebug("Finished retrieving {relativeUrl}", relativeUrl);

		return result;
	}

	/// <summary>Disconnects a worker's client, after the server said it has too many connections.</summary>
	public async Task DisconnectAsync(string workerName)
	{
		if (_clients.TryRemove(workerName, out AsyncFtpClient client))
		{
			try
			{
				await client.Disconnect();
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error disconnecting FTP connection.");
			}
			finally
			{
				await client.DisposeAsync();
			}
		}
	}

	/// <summary>
	/// Connects once to read the server's banner and help, trying the encryption modes one by one (auto detection
	/// is not reliable); the mode that works is used for the scan. Null when no mode worked.
	/// </summary>
	public async Task<string> GetServerInfoAsync(WebDirectory webDirectory, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeLimit.CancelAfter(DirectoryTimeLimit);

		GetCredentials(webDirectory, out string username, out string password);

		foreach (FtpEncryptionMode ftpEncryptionMode in Enum.GetValues<FtpEncryptionMode>())
		{
			try
			{
				logger.LogWarning("Try FTP(S) connection with EncryptionMode {ftpEncryptionMode}", ftpEncryptionMode);

				await using AsyncFtpClient client = CreateClient(webDirectory, username, password, ftpEncryptionMode);

				await client.Connect(timeLimit.Token);

				_encryptionMode = ftpEncryptionMode;

				FtpReply connectReply = client.LastReply;
				FtpReply helpReply = await client.Execute("HELP", timeLimit.Token);
				FtpReply statusReply = await client.Execute("STAT", timeLimit.Token);
				FtpReply systemReply = await client.Execute("SYST", timeLimit.Token);

				await client.Disconnect(timeLimit.Token);

				return
					$"Connect Response: {connectReply.InfoMessages}{Environment.NewLine}" +
					$"ServerType: {client.ServerType}{Environment.NewLine}" +
					$"Help response: {helpReply.InfoMessages}{Environment.NewLine}" +
					$"Status response: {statusReply.InfoMessages}{Environment.NewLine}" +
					$"System response: {systemReply.InfoMessages}{Environment.NewLine}";
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				if (ex is FtpCommandException ftpCommandException && IsMaxThreads(ftpCommandException))
				{
					return null;
				}

				logger.LogError("FTP EncryptionMode {ftpEncryptionMode} failed: {error}", ftpEncryptionMode, ex.Message);
			}
		}

		return null;
	}

	/// <summary>Disconnects every client, except the one kept for the speedtest.</summary>
	public async Task CloseAllAsync(AsyncFtpClient except = null)
	{
		foreach ((string workerName, AsyncFtpClient client) in _clients)
		{
			if (client == except)
			{
				continue;
			}

			try
			{
				await client.Disconnect();
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Error disconnecting FTP connection of {thread}", workerName);
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		foreach (AsyncFtpClient client in _clients.Values)
		{
			try
			{
				await client.DisposeAsync();
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Error disposing FTP connection");
			}
		}

		_clients.Clear();
	}

	private static AsyncFtpClient CreateClient(WebDirectory webDirectory, string username, string password, FtpEncryptionMode encryptionMode)
	{
		int timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;

		return new AsyncFtpClient(webDirectory.Uri.Host, webDirectory.Uri.Port)
		{
			Credentials = new NetworkCredential(username, password),
			Config = new FtpConfig
			{
				ConnectTimeout = timeout,
				DataConnectionConnectTimeout = timeout,
				DataConnectionReadTimeout = timeout,
				ReadTimeout = timeout * 2,
				EncryptionMode = encryptionMode,
				ValidateAnyCertificate = true
			}
		};
	}

	/// <summary>
	/// Whether the error says the server has too many connections. If it tells its limit, the scan's concurrency
	/// goes to that (one less for the "too many connections (N)" variant), else one lower than it is.
	/// </summary>
	private bool IsMaxThreads(FtpCommandException ftpCommandException)
	{
		string message = ftpCommandException.Message;

		foreach (Regex regex in RegexesThreads)
		{
			Match regexMatch = regex.Match(message);

			if (!regexMatch.Success)
			{
				continue;
			}

			ConcurrencyGovernor concurrency = session.Concurrency;
			int previous = concurrency.Effective;
			int limit;

			if (regexMatch.Groups.TryGetValue("MaxThreads", out Group maxThreadsGroup) && maxThreadsGroup.Success && int.TryParse(maxThreadsGroup.Value, out int maxThreads))
			{
				limit = regex == RegexesThreads[0] ? maxThreads - 1 : maxThreads;
			}
			else
			{
				limit = previous - 1;
			}

			concurrency.SetCap(ConcurrencyReasons.FtpMaxConnections, Math.Max(1, limit));

			if (concurrency.Effective != previous)
			{
				logger.LogWarning("Max threads reduced to {maxThreads}", concurrency.Effective);
			}

			return true;
		}

		foreach (Regex regex in RegexesConnections)
		{
			if (regex.IsMatch(message))
			{
				throw ftpCommandException;
			}
		}

		return false;
	}

	private void GetCredentials(WebDirectory webDirectory, out string username, out string password)
	{
		username = credentials.Username;
		password = credentials.Password;

		if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
		{
			return;
		}

		username = "anonymous";
		password = "password";

		if (webDirectory.Uri.UserInfo?.Contains(':') != true)
		{
			return;
		}

		string[] splitted = webDirectory.Uri.UserInfo.Split(':');

		username = WebUtility.UrlDecode(splitted.First());
		password = WebUtility.UrlDecode(splitted.Last());
	}

	// The specific ones first: they tell the limit
	private static readonly Regex[] RegexesThreads =
	[
		MaxThreadsSpecific01Regex(),
		MaxThreadsSpecific02Regex(),
		MaxThreadsSpecific03Regex(),
		MaxThreadsSpecific04Regex(),
		MaxThreadsGeneral01Regex(),
		MaxThreadsGeneral02Regex(),
		MaxThreadsGeneral03Regex(),
		MaxThreadsGeneral04Regex(),
	];

	private static readonly Regex[] RegexesConnections =
	[
		MaxUsersGeneral01Regex(),
		MaxUsersGeneral02Regex(),
		MaxUsersGeneral03Regex(),
	];

	[GeneratedRegex(@"Too many connections \((?<MaxThreads>\d*)\) from this IP")]
	private static partial Regex MaxThreadsSpecific01Regex();

	[GeneratedRegex(@"Sorry, the maximum number of clients \((?<MaxThreads>\d*)\) from your host are already connected.")]
	private static partial Regex MaxThreadsSpecific02Regex();

	[GeneratedRegex(@"Sorry, your system may not connect more than (?<MaxThreads>\d*) times.")]
	private static partial Regex MaxThreadsSpecific03Regex();

	[GeneratedRegex(@"Not logged in, only (?<MaxThreads>\d*) sessions from same IP allowed concurrently.")]
	private static partial Regex MaxThreadsSpecific04Regex();

	[GeneratedRegex(@"Too many connections")]
	private static partial Regex MaxThreadsGeneral01Regex();

	[GeneratedRegex(@"There are too many connections from your internet address.")]
	private static partial Regex MaxThreadsGeneral02Regex();

	[GeneratedRegex(@"No more connections allowed from your IP.")]
	private static partial Regex MaxThreadsGeneral03Regex();

	[GeneratedRegex(@"Sorry, the maximum number of clients \(\d*\) for this user are already connected.")]
	private static partial Regex MaxThreadsGeneral04Regex();

	[GeneratedRegex(@"Too many users logged in for this account.*")]
	private static partial Regex MaxUsersGeneral01Regex();

	[GeneratedRegex(@"There are too many connected users, please try later.")]
	private static partial Regex MaxUsersGeneral02Regex();

	[GeneratedRegex(@"Xlight (personal edition )?only allows (?<MaxThreads>\d*) online users at the same time")]
	private static partial Regex MaxUsersGeneral03Regex();
}
