using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Outputs;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class SpeedtestTests
{
	private const string Url = "https://example.com/files/café.mp3";

	/// <summary>Answers 200, and reports the request URI as the handler was told to (like HttpCloak does: percent-encoded)</summary>
	private sealed class FakeHandler(string reportedUri) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(new byte[64 * 1024]),
				RequestMessage = new HttpRequestMessage(HttpMethod.Get, reportedUri)
			});
		}
	}

	[Theory]
	[InlineData("https://example.com/files/caf%C3%A9.mp3")]
	[InlineData(Url)]
	public async Task SameUrlInAnotherForm_IsNotARedirect(string reportedUri)
	{
		using HttpClient client = new(new FakeHandler(reportedUri));

		SpeedtestResult result = await Speedtest.HttpAsync(client, Url, NullLogger.Instance, seconds: 1, TestContext.Current.CancellationToken);

		Assert.Equal(64 * 1024, result.DownloadedBytes);
	}

	[Fact]
	public async Task RealRedirect_CancelsTheSpeedtest()
	{
		using HttpClient client = new(new FakeHandler("https://example.com/login"));

		SpeedtestResult result = await Speedtest.HttpAsync(client, Url, NullLogger.Instance, seconds: 1, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.DownloadedBytes);
	}

	/// <summary>A stream that sends the given bytes, then nothing at all (until the read is cancelled)</summary>
	private sealed class StallingStream(int bytes) : Stream
	{
		private int sent;

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (sent < bytes)
			{
				int count = Math.Min(buffer.Length, bytes - sent);
				sent += count;

				return count;
			}

			await Task.Delay(Timeout.Infinite, cancellationToken);

			return 0;
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private sealed class StallingHandler(int bytes) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StreamContent(new StallingStream(bytes)),
				RequestMessage = request
			});
		}
	}

	[Fact]
	public async Task NoDataAtAll_StopsAfterTheStallTimeout()
	{
		using HttpClient client = new(new StallingHandler(0));

		Task<SpeedtestResult> speedtest = Speedtest.HttpAsync(client, Url, NullLogger.Instance, seconds: 25, TestContext.Current.CancellationToken, stallTimeout: TimeSpan.FromMilliseconds(300));

		Assert.Same(speedtest, await Task.WhenAny(speedtest, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
		Assert.Equal(0, (await speedtest).DownloadedBytes);
	}

	[Fact]
	public async Task DataThatStops_StopsAfterTheStallTimeout_AndKeepsWhatWasDownloaded()
	{
		using HttpClient client = new(new StallingHandler(50_000));

		Task<SpeedtestResult> speedtest = Speedtest.HttpAsync(client, Url, NullLogger.Instance, seconds: 25, TestContext.Current.CancellationToken, stallTimeout: TimeSpan.FromMilliseconds(300));

		Assert.Same(speedtest, await Task.WhenAny(speedtest, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
		Assert.Equal(50_000, (await speedtest).DownloadedBytes);
	}
}
