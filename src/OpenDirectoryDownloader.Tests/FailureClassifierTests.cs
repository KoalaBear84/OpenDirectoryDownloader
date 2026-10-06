using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Crawling;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Threading;
using System.Net;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>When a failed directory is tried again, and when the crawler gives up on it.</summary>
public class FailureClassifierTests
{
	private static (FailureClassifier Classifier, ScanSession Session) Create(int threads = 10)
	{
		ScanSession session = new(new WebDirectory(null) { Url = "http://example.com/" }, threads);

		return (new FailureClassifier(session, NullLogger<FailureClassifier>.Instance), session);
	}

	private static readonly WebDirectory Directory = new(null) { Url = "http://example.com/some/dir/" };

	private static bool ShouldRetry(Exception ex, int attempt, ScanSession session = null, WebDirectory directory = null)
	{
		(FailureClassifier classifier, ScanSession created) = Create();

		if (session is not null)
		{
			classifier = new FailureClassifier(session, NullLogger<FailureClassifier>.Instance);
		}

		return classifier.ShouldRetry(ex, attempt, directory ?? Directory, "P1", TimeSpan.FromSeconds(2));
	}

	private static HttpRequestException Http(HttpStatusCode? statusCode, string message = "error") => new(message, null, statusCode);

	[Fact]
	public void Cancel_GivesUpRightAway() => Assert.False(ShouldRetry(new CancelException("no"), 1));

	[Fact]
	public void SilentAndSoftRateLimit_AreAlwaysRetried()
	{
		Assert.True(ShouldRetry(new SilentException(), 50));
		Assert.True(ShouldRetry(new SoftRateLimitException(), 50));
	}

	[Theory]
	[InlineData(1, true)]
	[InlineData(4, true)]
	[InlineData(5, false)]
	public void Timeout_IsRetriedFourTimes(int attempt, bool retried)
	{
		Assert.Equal(retried, ShouldRetry(new TaskCanceledException("timeout", new TimeoutException()), attempt));
	}

	[Theory]
	[InlineData(1, true)]
	[InlineData(4, true)]
	[InlineData(5, false)]
	public void OtherErrors_AreRetriedFourTimes(int attempt, bool retried)
	{
		Assert.Equal(retried, ShouldRetry(new InvalidOperationException("boom"), attempt));
		Assert.Equal(retried, ShouldRetry(Http(HttpStatusCode.InternalServerError), attempt));
	}

	[Fact]
	public void KnownErrorPaths_GiveUpRightAway()
	{
		Assert.False(ShouldRetry(Http(HttpStatusCode.InternalServerError), 1, directory: new WebDirectory(null) { Url = "http://example.com/cgi-bin/" }));
	}

	[Theory]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	[InlineData(HttpStatusCode.TooManyRequests)]
	[InlineData(HttpStatusCode.LoopDetected)]
	public void RateLimits_AreRetried_AndLowerTheThreads(HttpStatusCode statusCode)
	{
		(FailureClassifier classifier, ScanSession session) = Create(threads: 20);

		Assert.True(classifier.ShouldRetry(Http(statusCode), 30, Directory, "P1", TimeSpan.Zero));
		Assert.Equal(FailureClassifier.RateLimitedThreads, session.Concurrency.Effective);
	}

	[Fact]
	public void NotFound_GivesUp_ExceptOnAGoogleDriveIndex()
	{
		Assert.False(ShouldRetry(Http(HttpStatusCode.NotFound), 1));

		(FailureClassifier _, ScanSession session) = Create();
		session.SiteState.Get<GoogleDriveIndexState>().IsGoogleDriveIndex = true;

		Assert.True(ShouldRetry(Http(HttpStatusCode.NotFound), 1, session));
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.Unauthorized)]
	public void Forbidden_IsTriedTwice(HttpStatusCode statusCode)
	{
		Assert.True(ShouldRetry(Http(statusCode), 2));
		Assert.False(ShouldRetry(Http(statusCode), 3));
	}

	[Fact]
	public void UnknownHost_GivesUp()
	{
		Assert.False(ShouldRetry(Http(null, "No such host is known."), 1));
	}
}
