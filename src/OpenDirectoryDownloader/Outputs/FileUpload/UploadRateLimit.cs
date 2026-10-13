using Microsoft.Extensions.Logging;
using System.Net;

namespace OpenDirectoryDownloader.Outputs.FileUpload;

/// <summary>The upload site says it is asked too much (HTTP 429): don't try this site again, go on with the next one</summary>
public sealed class UploadRateLimitedException(string siteName) : Exception($"{siteName} is rate limiting (HTTP 429)");

public static class UploadRateLimit
{
	/// <summary>A Retry-After longer than this is not waited for</summary>
	public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);

	/// <summary>
	/// Does nothing unless the response is HTTP 429. Then it waits when the site says when to come back (Retry-After) and that is
	/// within <see cref="MaxWait"/>, and always throws a <see cref="UploadRateLimitedException"/>, so the site is not tried again.
	/// </summary>
	public static async Task CheckAsync(HttpResponseMessage httpResponseMessage, string siteName, ILogger logger, CancellationToken cancellationToken = default)
	{
		if (httpResponseMessage.StatusCode != HttpStatusCode.TooManyRequests)
		{
			return;
		}

		TimeSpan? retryAfter = httpResponseMessage.Headers.RetryAfter switch
		{
			{ Delta: TimeSpan delta } => delta,
			{ Date: DateTimeOffset date } => date - DateTimeOffset.UtcNow,
			_ => null
		};

		if (retryAfter is TimeSpan wait && wait <= MaxWait)
		{
			wait = wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
			logger.LogWarning("{siteName} is rate limiting (HTTP 429), waiting {seconds:F0} seconds as it asked, then going on with the next site", siteName, wait.TotalSeconds);
			await Task.Delay(wait, cancellationToken);
		}
		else
		{
			logger.LogWarning("{siteName} is rate limiting (HTTP 429){retryAfter}, going on with the next site", siteName, retryAfter is TimeSpan longWait ? $" and wants a wait of {longWait.TotalSeconds:F0} seconds" : string.Empty);
		}

		throw new UploadRateLimitedException(siteName);
	}
}
