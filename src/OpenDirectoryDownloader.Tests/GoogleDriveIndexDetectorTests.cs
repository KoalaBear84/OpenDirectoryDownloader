using OpenDirectoryDownloader.Scanning;
using System.Net;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// The check of an app.js script is only a guess whether the page is a Google Drive index: a script that isn't there
/// must not fail the page, and is not asked for again and again.
/// </summary>
public class GoogleDriveIndexDetectorTests
{
	private sealed class RecordingHandler(HttpStatusCode statusCode, string content = "") : HttpMessageHandler
	{
		public List<string> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request.RequestUri.ToString());

			return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(content) });
		}
	}

	private const string Page = "<html><head><script src=\"js/app.js\"></script></head><body><pre><a href=\"a.txt\">a.txt</a>  02-Jan-2024 10:11   100\n</pre></body></html>";

	private static ParserServices Services(HttpMessageHandler handler) => new()
	{
		Session = new ScanSession(new WebDirectory(null) { Url = "http://localhost/", Name = Constants.Root }),
		Http = new HttpClient(handler)
	};

	[Fact]
	public async Task AMissingAppJs_DoesNotFailThePage_AndIsAskedForOnce()
	{
		RecordingHandler handler = new(HttpStatusCode.NotFound);
		ParserServices services = Services(handler);

		WebDirectory first = await HtmlListingParser.ParseAsync(services, new WebDirectory(null) { Url = "http://localhost/sub/" }, Page);
		WebDirectory second = await HtmlListingParser.ParseAsync(services, new WebDirectory(null) { Url = "http://localhost/sub/" }, Page);

		Assert.False(first.Error);
		Assert.False(second.Error);
		Assert.Single(first.Files);

		// Relative to the page, not to the root of the site, and not again for the same script
		Assert.Equal(["http://localhost/sub/js/app.js"], handler.Requests);
	}

	[Fact]
	public async Task AnAppJsThatIsNotJavaScript_DoesNotFailThePage()
	{
		RecordingHandler handler = new(HttpStatusCode.OK, "<html>this is not (JavaScript");

		WebDirectory result = await HtmlListingParser.ParseAsync(Services(handler), new WebDirectory(null) { Url = "http://localhost/" }, Page);

		Assert.False(result.Error);
		Assert.Single(result.Files);
	}
}
