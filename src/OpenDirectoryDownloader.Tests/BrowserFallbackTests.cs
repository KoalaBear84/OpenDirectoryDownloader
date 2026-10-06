using OpenDirectoryDownloader.Scanning;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// A page with nothing in it but a &lt;noscript&gt; is probably drawn by JavaScript: it is fetched once more
/// through a browser. The old condition was inverted, so this never happened.
/// </summary>
public class BrowserFallbackTests
{
	private sealed class FakeBrowser(string html) : IBrowserRenderer
	{
		public int Calls { get; private set; }

		public Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken = default)
		{
			Calls++;

			return Task.FromResult(html);
		}
	}

	private const string JavaScriptPage = "<html><head><script src=\"main.js\"></script></head><body><noscript>Please enable JavaScript</noscript><div id=\"app\"></div></body></html>";

	private const string RenderedPage = "<html><body><pre><a href=\"a.txt\">a.txt</a>  02-Jan-2024 10:11   100\n<a href=\"sub/\">sub/</a>  02-Jan-2024 10:11    -\n</pre></body></html>";

	private static ParserServices Services(IBrowserRenderer browser, bool noBrowser = false) => new()
	{
		Session = new ScanSession(new WebDirectory(null) { Url = "http://localhost/", Name = Constants.Root }),
		Http = new HttpClient(),
		Browser = browser,
		Options = new ParserOptions { NoBrowser = noBrowser }
	};

	[Fact]
	public async Task EmptyJavaScriptPage_IsParsedFromTheBrowser_OncePerUrl()
	{
		FakeBrowser browser = new(RenderedPage);
		ParserServices services = Services(browser);

		WebDirectory result = await HtmlListingParser.ParseAsync(services, new WebDirectory(null) { Url = "http://localhost/" }, JavaScriptPage);

		Assert.Equal(1, browser.Calls);
		Assert.Equal("a.txt", Assert.Single(result.Files).FileName);
		Assert.Equal("sub", Assert.Single(result.Subdirectories).Name);

		// The same URL again: not through the browser again
		await HtmlListingParser.ParseAsync(services, new WebDirectory(null) { Url = "http://localhost/" }, JavaScriptPage);
		Assert.Equal(1, browser.Calls);
	}

	[Fact]
	public async Task NoBrowser_IsRespected()
	{
		FakeBrowser browser = new(RenderedPage);

		WebDirectory result = await HtmlListingParser.ParseAsync(Services(browser, noBrowser: true), new WebDirectory(null) { Url = "http://localhost/" }, JavaScriptPage);

		Assert.Equal(0, browser.Calls);
		Assert.Empty(result.Files);
	}

	[Fact]
	public async Task APageWithAListing_DoesNotNeedTheBrowser()
	{
		FakeBrowser browser = new(RenderedPage);

		WebDirectory result = await HtmlListingParser.ParseAsync(Services(browser), new WebDirectory(null) { Url = "http://localhost/" }, RenderedPage.Replace("</body>", "<noscript>x</noscript><script></script></body>"));

		Assert.Equal(0, browser.Calls);
		Assert.Single(result.Files);
	}
}
