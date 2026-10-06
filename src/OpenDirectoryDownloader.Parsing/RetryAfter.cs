using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using System.Net.Http.Headers;

namespace OpenDirectoryDownloader.Parsing;

public static class RetryAfter
{
	/// <summary>
	/// When the server says when to come back (Retry-After), waits that long and throws a <see cref="SilentException"/>
	/// so the caller's retry tries again without logging it twice. Does nothing without the header.
	/// </summary>
	public static async Task WaitIfAskedAsync(ILogger logger, string threadName, WebDirectory webDirectory, HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken = default)
	{
		if (httpResponseMessage.Headers.RetryAfter is not RetryConditionHeaderValue retryConditionHeaderValue)
		{
			return;
		}

		TimeSpan wait;

		if (retryConditionHeaderValue.Date is DateTimeOffset dateTimeOffset)
		{
			wait = DateTimeOffset.UtcNow - dateTimeOffset;
			logger.LogWarning("[{thread}] Rate limited on Url '{url}'. Need to wait until {untilDate} ({waitTime:F1}) seconds.", threadName, webDirectory.Url, retryConditionHeaderValue.Date, wait.TotalSeconds);
		}
		else if (retryConditionHeaderValue.Delta is TimeSpan timeSpan)
		{
			wait = timeSpan;
			logger.LogWarning("[{thread}] Rate limited on Url '{url}'. Need to wait for {waitTime:F1} seconds ({untilDate:HH:mm:ss}).", threadName, webDirectory.Url, timeSpan.TotalSeconds, DateTime.Now.Add(timeSpan));
		}
		else
		{
			return;
		}

		httpResponseMessage.Dispose();

		if (wait > TimeSpan.Zero)
		{
			await Task.Delay(wait, cancellationToken);
		}

		throw new SilentException();
	}
}
