using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Threading;
using System.Net;
using System.Security.Authentication;

namespace OpenDirectoryDownloader.Crawling;

/// <summary>
/// Decides, after a directory failed, whether to try it again or give up on it, and says why. The checks are in
/// a deliberate order: specific errors (rate limits, known broken paths) before the generic "try a few times".
/// </summary>
public sealed class FailureClassifier(ScanSession session, ILogger<FailureClassifier> logger)
{
	/// <summary>How many times a generic error is retried</summary>
	public const int FallbackRetryCount = 4;

	/// <summary>Servers rate limiting or refusing connections get at most this many workers</summary>
	public const int RateLimitedThreads = 5;

	private static readonly string[] KnownErrorPaths =
	[
		"cgi-bin/",
		"lost%2Bfound/"
	];

	/// <param name="attempt">The attempt that failed, 1 for the first</param>
	/// <param name="wait">The wait before the next attempt, for the log</param>
	/// <returns>True to try again</returns>
	public bool ShouldRetry(Exception ex, int attempt, WebDirectory webDirectory, string threadName, TimeSpan wait)
	{
		string relativeUrl = webDirectory.Uri.PathAndQuery;
		double waitTime = wait.TotalSeconds;

		switch (ex)
		{
			case CancelException:
				logger.LogWarning("[{thread}] Cancelling: {error}", threadName, ex.Message);
				return false;
			case SilentException:
				return true;
			case SoftRateLimitException:
				logger.LogWarning("[{thread}] Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, attempt, relativeUrl, waitTime);
				return true;
			case TaskCanceledException { InnerException: TimeoutException } when attempt <= FallbackRetryCount:
				logger.LogWarning("[{thread}] Timeout (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, attempt, relativeUrl, waitTime);
				return true;
			case HttpRequestException httpRequestException:
				return ShouldRetry(httpRequestException, attempt, webDirectory, threadName, relativeUrl, waitTime);
		}

		if (attempt <= FallbackRetryCount)
		{
			logger.LogWarning("[{thread}] Error '{error}' retrieving on try {retryCount} for url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, GetExceptionWithInner(ex), attempt, relativeUrl, waitTime);
			return true;
		}

		logger.LogWarning("[{thread}] Cancelling on try {retryCount} for url '{relativeUrl}'.", threadName, attempt, relativeUrl);
		return false;
	}

	private bool ShouldRetry(HttpRequestException ex, int attempt, WebDirectory webDirectory, string threadName, string relativeUrl, double waitTime)
	{
		if (OperatingSystem.IsWindows() && ex.InnerException is AuthenticationException)
		{
			logger.LogWarning("[{thread}] Please check readme to fix possible TLS 1.3 issue: https://github.com/KoalaBear84/OpenDirectoryDownloader/#tls-errors-windows-10", threadName);
		}

		int httpStatusCode = (int)(ex.StatusCode ?? 0);

		if (KnownErrorPaths.Contains(webDirectory.Uri.Segments.LastOrDefault()))
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Cancelling known error on try {retryCount} for url '{relativeUrl}'.", threadName, httpStatusCode, attempt, relativeUrl);
			return false;
		}

		if (ex.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Rate limited (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, attempt, relativeUrl, waitTime);
			RateLimited();
			return true;
		}

		if (ex.StatusCode == HttpStatusCode.LoopDetected)
		{
			// A Retry-After header could say up to 4 hours, but a request mostly works again after a few seconds
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Rate limited / out of capacity (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, attempt, relativeUrl, waitTime);
			RateLimited();
			return true;
		}

		if (ex.Message.Contains("No connection could be made because the target machine actively refused it."))
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Rate limited? (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, attempt, relativeUrl, waitTime);
			RateLimited();
			return true;
		}

		// A Google Drive index answers 404 for a folder it is still loading
		if (session.SiteState.Find<GoogleDriveIndexState>()?.IsGoogleDriveIndex != true && (ex.StatusCode == HttpStatusCode.NotFound || ex.Message == "No such host is known."))
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Error '{error}' (try {retryCount}). Url '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, ex.Message, attempt, relativeUrl, waitTime);
			return false;
		}

		if (ex.StatusCode is null && ex.InnerException?.Message == "The requested name is valid, but no data of the requested type was found.")
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Domain does not exist? Possible DNS issue. Skipping..", threadName, httpStatusCode);
			return false;
		}

		if (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized && attempt >= 3)
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Error '{error}' retrieving on try {retryCount}) for '{relativeUrl}'. Skipping..", threadName, httpStatusCode, ex.Message, attempt, relativeUrl);
			return false;
		}

		if (attempt <= FallbackRetryCount)
		{
			logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Error '{error}' retrieving on try {retryCount}) for '{relativeUrl}'. Waiting {waitTime:F0} seconds.", threadName, httpStatusCode, GetExceptionWithInner(ex), attempt, relativeUrl, waitTime);
			return true;
		}

		logger.LogWarning("[{thread}] HTTP {httpStatusCode}. Cancelling on try {retryCount} for url '{relativeUrl}'.", threadName, httpStatusCode, attempt, relativeUrl);
		return false;
	}

	/// <summary>The server is struggling: fewer workers from now on (they used to stop one by one down to this)</summary>
	private void RateLimited()
	{
		if (session.Concurrency.GetCap(ConcurrencyReasons.RateLimited) is null && session.Concurrency.Effective > RateLimitedThreads)
		{
			logger.LogWarning("Decrease threads to {threads} because of rate limiting", RateLimitedThreads);
		}

		session.Concurrency.SetCap(ConcurrencyReasons.RateLimited, RateLimitedThreads);
	}

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
}
