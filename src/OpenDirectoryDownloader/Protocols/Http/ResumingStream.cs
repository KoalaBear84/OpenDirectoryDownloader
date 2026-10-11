using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>
/// A response body that continues with a Range request when the server cuts the connection before the declared
/// Content-Length (seen with lighttpd on large listings: the transfer stops at the same byte every time, while a
/// Range request for the rest works fine).
/// </summary>
public sealed class ResumingStream : Stream
{
	private const int MaxResumes = 5;

	private readonly HttpClient httpClient;
	private readonly Uri uri;
	private readonly long totalLength;
	private readonly string validator;
	private readonly ILogger logger;
	private Stream inner;
	private HttpResponseMessage resumeResponse;
	private long position;
	private int resumes;
	private bool successLogged;

	private ResumingStream(Stream inner, HttpClient httpClient, Uri uri, long totalLength, string validator, ILogger logger)
	{
		this.inner = inner;
		this.httpClient = httpClient;
		this.uri = uri;
		this.totalLength = totalLength;
		this.validator = validator;
		this.logger = logger;
	}

	/// <summary>
	/// Makes the body of <paramref name="response"/> resumable when that is possible: a 200 with a known length, and
	/// a server that says it handles ranges. Compressed bodies are left alone, the offsets would be of the compressed data.
	/// </summary>
	public static async Task MakeResumableAsync(HttpResponseMessage response, HttpClient httpClient, ILogger logger, CancellationToken cancellationToken)
	{
		if (response is null
			|| response.StatusCode != HttpStatusCode.OK
			|| response.RequestMessage?.Method != HttpMethod.Get
			|| response.RequestMessage.RequestUri is not Uri uri
			|| response.Content.Headers.ContentLength is null or <= 0
			|| response.Content.Headers.ContentEncoding.Count > 0
			|| !response.Headers.AcceptRanges.Contains("bytes"))
		{
			return;
		}

		long length = response.Content.Headers.ContentLength.Value;
		string validator = response.Headers.ETag is { IsWeak: false } etag ? etag.ToString() : response.Content.Headers.LastModified?.ToString("R");
		Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

		StreamContent content = new(new ResumingStream(stream, httpClient, uri, length, validator, logger));

		foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
		{
			content.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		response.Content = content;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		while (true)
		{
			try
			{
				int read = await inner.ReadAsync(buffer, cancellationToken);
				position += read;

				if (read > 0 || position >= totalLength)
				{
					if (read == 0 && resumes > 0 && !successLogged)
					{
						successLogged = true;
						logger.LogWarning("Response for {url} was cut off by the server, but resumed with {resumes} Range request(s) and received completely ({length} bytes)", uri, resumes, totalLength);
					}

					return read;
				}

				// Ended without an error but before the declared length
			}
			catch (Exception ex) when (ex is IOException or HttpRequestException)
			{
				if (position >= totalLength || resumes >= MaxResumes)
				{
					throw;
				}

				logger.LogWarning("Response for {url} ended after {position} of {length} bytes ({error}), resuming with a Range request", uri, position, totalLength, ex.Message);
			}

			if (resumes >= MaxResumes || !await ResumeAsync(cancellationToken))
			{
				logger.LogError("Response for {url} ended after {position} of {length} bytes and could not be resumed (server ignored the Range request, the file changed, or {max} resumes were not enough)", uri, position, totalLength, MaxResumes);

				throw new IOException($"The response ended prematurely after {position} of {totalLength} bytes and could not be resumed");
			}
		}
	}

	private async Task<bool> ResumeAsync(CancellationToken cancellationToken)
	{
		resumes++;

		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		request.Headers.Range = new RangeHeaderValue(position, null);

		if (validator is not null)
		{
			request.Headers.TryAddWithoutValidation("If-Range", validator);
		}

		HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

		// 200 means the server ignored the range or the file changed: can't continue from here
		if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != position)
		{
			response.Dispose();
			return false;
		}

		Stream newStream = await response.Content.ReadAsStreamAsync(cancellationToken);

		await inner.DisposeAsync();
		resumeResponse?.Dispose();
		resumeResponse = response;
		inner = newStream;

		return true;
	}

	public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			inner.Dispose();
			resumeResponse?.Dispose();
		}

		base.Dispose(disposing);
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => position; set => throw new NotSupportedException(); }
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
