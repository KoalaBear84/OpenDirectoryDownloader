using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing;
using OpenDirectoryDownloader.Parsing.Sites.AmazonS3;
using OpenDirectoryDownloader.Scanning;
using System.Net;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>An S3 error (503 Slow Down) must reach the crawl engine, which retries; a retry must not duplicate entries.</summary>
public class AmazonS3ParserTests
{
	private const string Url = "https://bucket.s3.amazonaws.com/?delimiter=/&prefix=photos/";

	private static string Page(bool truncated, string key) =>
		$"""
		<?xml version="1.0" encoding="UTF-8"?>
		<ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
			<Name>bucket</Name>
			<Prefix>photos/</Prefix>
			<NextMarker>{key}</NextMarker>
			<IsTruncated>{truncated.ToString().ToLowerInvariant()}</IsTruncated>
			<Contents><Key>{key}</Key><Size>10</Size></Contents>
		</ListBucketResult>
		""";

	private sealed class SequenceHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
	{
		private int call;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responses[call++]());
	}

	private static HttpResponseMessage Ok(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml) };

	private static HttpResponseMessage SlowDown() => new(HttpStatusCode.ServiceUnavailable) { ReasonPhrase = "Slow Down" };

	private static (ParserServices Services, WebDirectory Directory) Create(HttpMessageHandler handler)
	{
		WebDirectory directory = new(null) { Url = Url, Name = Constants.Root };

		return (new ParserServices { Session = new ScanSession(directory), Http = new HttpClient(handler) }, directory);
	}

	[Fact]
	public async Task SlowDown_IsThrown_AndTheDirectoryIsNotMarkedAsFailed()
	{
		(ParserServices services, WebDirectory directory) = Create(new SequenceHandler(SlowDown));

		HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => AmazonS3Parser.ParseIndex(services, directory, hasHeader: true));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
		Assert.False(directory.Error);
	}

	[Fact]
	public async Task FailureOnALaterPage_LeavesNothingOnTheDirectory_SoARetryHasNoDuplicates()
	{
		SequenceHandler handler = new(() => Ok(Page(true, "photos/a.jpg")), SlowDown, () => Ok(Page(true, "photos/a.jpg")), () => Ok(Page(false, "photos/b.jpg")));
		(ParserServices services, WebDirectory directory) = Create(handler);

		await Assert.ThrowsAsync<HttpRequestException>(() => AmazonS3Parser.ParseIndex(services, directory, hasHeader: true));
		Assert.Empty(directory.Files);

		await AmazonS3Parser.ParseIndex(services, directory, hasHeader: true);

		Assert.Equal(["a.jpg", "b.jpg"], directory.Files.Select(f => f.FileName));
		Assert.False(directory.Error);
	}
}
