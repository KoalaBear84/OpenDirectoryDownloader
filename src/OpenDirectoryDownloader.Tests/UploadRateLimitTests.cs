using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Outputs.FileUpload;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class UploadRateLimitTests
{
	private static HttpResponseMessage Response(HttpStatusCode statusCode, TimeSpan? retryAfter)
	{
		HttpResponseMessage response = new(statusCode);

		if (retryAfter is not null)
		{
			response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
		}

		return response;
	}

	[Fact]
	public async Task NotRateLimited_DoesNothing()
	{
		await UploadRateLimit.CheckAsync(Response(HttpStatusCode.OK, null), "Test", NullLogger.Instance);
		await UploadRateLimit.CheckAsync(Response(HttpStatusCode.InternalServerError, TimeSpan.FromSeconds(1)), "Test", NullLogger.Instance);
	}

	[Fact]
	public async Task RetryAfterWithinTwoMinutes_Waits_ThenGivesUpOnTheSite()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		await Assert.ThrowsAsync<UploadRateLimitedException>(() => UploadRateLimit.CheckAsync(Response(HttpStatusCode.TooManyRequests, TimeSpan.FromMilliseconds(300)), "Test", NullLogger.Instance, TestContext.Current.CancellationToken));

		Assert.True(stopwatch.ElapsedMilliseconds >= 250, "It should have waited as asked");
	}

	[Theory]
	[InlineData(null)]
	[InlineData(3600)]
	public async Task NoRetryAfterOrTooLong_DoesNotWait_AndGivesUpOnTheSite(int? seconds)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		await Assert.ThrowsAsync<UploadRateLimitedException>(() => UploadRateLimit.CheckAsync(Response(HttpStatusCode.TooManyRequests, seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)), "Test", NullLogger.Instance, TestContext.Current.CancellationToken));

		Assert.True(stopwatch.ElapsedMilliseconds < 1000);
	}
}
