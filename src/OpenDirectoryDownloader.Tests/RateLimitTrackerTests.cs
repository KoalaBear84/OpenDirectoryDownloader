using OpenDirectoryDownloader.Crawling;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Scanning;
using System.Net;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class RateLimitTrackerTests
{
	[Fact]
	public void Waiting_IsCountedUntilDisposed_AndDisposingTwiceCountsOnce()
	{
		RateLimitTracker tracker = new();

		IDisposable first = tracker.BeginWait();
		using IDisposable second = tracker.BeginWait();
		Assert.Equal(2, tracker.Waiting);

		first.Dispose();
		first.Dispose();
		Assert.Equal(1, tracker.Waiting);
	}

	[Fact]
	public void AllWaiting_OnlyWhenEveryRunningWorkerWaits()
	{
		RateLimitTracker tracker = new();

		Assert.False(tracker.AllWaiting(0), "Nobody is waiting");
		Assert.False(tracker.AllWaiting(3));

		using IDisposable a = tracker.BeginWait();
		using IDisposable b = tracker.BeginWait();
		Assert.False(tracker.AllWaiting(3), "One worker is still downloading");

		using IDisposable c = tracker.BeginWait();
		Assert.True(tracker.AllWaiting(3));
	}

	[Fact]
	public void IsProgressing_NeedsARecentSuccess()
	{
		RateLimitTracker tracker = new();

		Assert.False(tracker.IsProgressing(), "Nothing succeeded yet");

		tracker.ReportSuccess();
		Assert.True(tracker.IsProgressing());
		Assert.False(tracker.IsProgressing(TimeSpan.FromTicks(-1)), "Outside the window");
	}

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests, true)]
	[InlineData(HttpStatusCode.ServiceUnavailable, true)]
	[InlineData(HttpStatusCode.LoopDetected, true)]
	[InlineData(HttpStatusCode.NotFound, false)]
	[InlineData(HttpStatusCode.Forbidden, false)]
	public void IsRateLimit_RecognizesTheStatusCodes(HttpStatusCode statusCode, bool expected)
	{
		Assert.Equal(expected, FailureClassifier.IsRateLimit(new HttpRequestException("error", null, statusCode)));
	}

	[Fact]
	public void IsRateLimit_SoftRateLimitYes_OtherErrorsNo()
	{
		Assert.True(FailureClassifier.IsRateLimit(new SoftRateLimitException()));
		Assert.False(FailureClassifier.IsRateLimit(new IOException()));
	}
}
