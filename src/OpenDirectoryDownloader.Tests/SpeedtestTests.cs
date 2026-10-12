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
}
