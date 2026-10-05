using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Tests.Integration;
using System.Text;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// RawPreListing lets ParseHtml skip building a DOM for the lines of a big listing's one &lt;pre&gt;. It is only a
/// shortcut, so the result must be identical to parsing the page the normal way: these run the same pages both ways.
/// </summary>
public class RawPreListingTests
{
	public RawPreListingTests()
	{
		Program.Logger ??= new Serilog.LoggerConfiguration().CreateLogger();
	}

	private static string Describe(WebDirectory directory)
	{
		StringBuilder text = new();
		text.Append($"url={directory.Url} name={directory.Name} desc={directory.Description} parser={directory.Parser} error={directory.Error} ok={directory.ParsedSuccessfully}\n");

		foreach (WebDirectory subdirectory in directory.Subdirectories)
		{
			text.Append($"  dir {subdirectory.Url} | {subdirectory.Name} | {subdirectory.Description} | {subdirectory.Parser}\n");
		}

		foreach (WebFile file in directory.Files)
		{
			text.Append($"  file {file.Url} | {file.FileName} | {file.FileSize} | {file.Description}\n");
		}

		return text.ToString();
	}

	private static async Task<string> Parse(string html, int minimumLength)
	{
		RawPreListing.MinimumLength = minimumLength;

		try
		{
			WebDirectory directory = new(new WebDirectory(null) { Url = "http://localhost:8086/", Name = "root" }) { Url = "http://localhost:8086/listing/", Name = "listing" };

			return Describe(await DirectoryParser.ParseHtml(directory, html));
		}
		catch (Exception ex)
		{
			// Both ways must at least fail the same way
			return "EXCEPTION " + ex.GetType().Name;
		}
	}

	private static async Task AssertSameBothWays(string html, string what)
	{
		string normal = await Parse(html, int.MaxValue);
		string shortcut = await Parse(html, 0);

		Assert.True(normal == shortcut, $"Different result with the shortcut for {what}{Environment.NewLine}--- normal{Environment.NewLine}{normal[..Math.Min(600, normal.Length)]}{Environment.NewLine}--- shortcut{Environment.NewLine}{shortcut[..Math.Min(600, shortcut.Length)]}");
	}

	[Fact]
	public async Task EverySamplePage_GivesTheSameResultBothWays_AndTheShortcutIsActuallyUsed()
	{
		int pages = 0;
		int shortcutPages = 0;

		foreach (string path in Directory.EnumerateFiles("Samples", "*.dat"))
		{
			string html = File.ReadAllText(path);
			pages++;

			if (RawPreListing.TryExtract(html) is not null)
			{
				shortcutPages++;
			}

			await AssertSameBothWays(html, path);
		}

		Assert.True(pages > 200, $"Expected the whole sample corpus, found {pages} pages");
		Assert.True(shortcutPages >= 10, $"The shortcut should apply to a fair share of real pages, applied to {shortcutPages} of {pages}");
	}

	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	[InlineData(ListingStyle.HtmlTable)]
	public async Task GeneratedListings_GiveTheSameResultBothWays(ListingStyle style)
	{
		FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 1, SubdirectoriesPerDirectory = 4, FilesPerDirectory = 1_500 });
		string html = site.RenderListing("/dir-01/");

		Assert.Equal(style != ListingStyle.HtmlTable, RawPreListing.TryExtract(html) is not null);

		await AssertSameBothWays(html, style.ToString());
	}

	[Theory]
	// An entity in one line: the whole page is parsed the normal way
	[InlineData("<html><body><pre><a href=\"a.txt\">a.txt</a> 1\n<a href=\"b&amp;c.txt\">b&amp;c.txt</a> 2\n</pre></body></html>")]
	// A leading newline after <pre> (dropped by the HTML parser)
	[InlineData("<html><body><pre>\n<a href=\"a.txt\">a.txt</a>     02-Jan-2024 10:11   100\n<a href=\"d/\">d/</a>   02-Jan-2024 10:11    -\n</pre></body></html>")]
	// CRLF line ends (nginx, Windows servers), a lone CR, and CRLF straight after <pre>
	[InlineData("<html><body><pre>\r\n<a href=\"a.txt\">a.txt</a>     02-Jan-2024 10:11   100\r\n<a href=\"d/\">d/</a>   02-Jan-2024 10:11    -\r\n</pre></body></html>")]
	[InlineData("<html><body><pre><a href=\"a.txt\">a.txt</a>     02-Jan-2024 10:11   100\r<a href=\"d/\">d/</a>   02-Jan-2024 10:11    -\r\r\n</pre></body></html>")]
	// <hr> separators inside the <pre>
	[InlineData("<html><body><pre><hr><a href=\"a.txt\">a.txt</a>    02-Jan-2024 10:11   100\n<hr><a href=\"b.txt\">b.txt</a>    02-Jan-2024 10:11   200\n<hr></pre></body></html>")]
	// A non-breaking space and a bare '>' in text: the serializer would write those differently, so no shortcut
	[InlineData("<html><body><pre><a href=\"a.txt\">a.txt</a>  02-Jan-2024 10:11   100\n<a href=\"b.txt\">b > c.txt</a>  02-Jan-2024 10:11   200\n</pre></body></html>")]
	// Attributes a selector could care about
	[InlineData("<html><body><pre><a class=\"breadcrumb\" href=\"a.txt\">a.txt</a>  02-Jan-2024 10:11   100\n<a href=\"b.txt\">b.txt</a>  02-Jan-2024 10:11   200\n</pre></body></html>")]
	// Two <pre> elements, an uppercase one, one in a script, one with attributes
	[InlineData("<html><body><pre><a href=\"a.txt\">a.txt</a> 02-Jan-2024 10:11 100\n</pre><pre><a href=\"b.txt\">b.txt</a> 02-Jan-2024 10:11 200\n</pre></body></html>")]
	[InlineData("<html><body><PRE><a href=\"a.txt\">a.txt</a> 02-Jan-2024 10:11 100\n</PRE></body></html>")]
	[InlineData("<html><body><script>var s = '<pre><a href=\"x\">x</a></pre>';</script><pre><a href=\"a.txt\">a.txt</a> 02-Jan-2024 10:11 100\n</pre></body></html>")]
	[InlineData("<html><body><pre class=\"listing\"><a href=\"a.txt\">a.txt</a> 02-Jan-2024 10:11 100\n</pre></body></html>")]
	// The only <pre> sits in a comment or a textarea, so there is no <pre> in the DOM at all
	[InlineData("<html><body><!-- <pre><a href=\"x\">x</a></pre> --><a href=\"a.txt\">a.txt</a><a href=\"b.txt\">b.txt</a></body></html>")]
	[InlineData("<html><body><textarea><pre><a href=\"x\">x</a></pre></textarea><a href=\"a.txt\">a.txt</a></body></html>")]
	// A <pre> that gives nothing: the links elsewhere on the page must still be found
	[InlineData("<html><body><pre>no listing in here\n</pre><ul><li><a href=\"a.txt\">a.txt</a></li><li><a href=\"b.txt\">b.txt</a></li></ul></body></html>")]
	[InlineData("<html><body><pre><a href=\"../\">../</a>\n</pre><a href=\"a.txt\">a.txt</a><a href=\"b.txt\">b.txt</a></body></html>")]
	// Unclosed or odd structure
	[InlineData("<html><body><pre><a href=\"a.txt\">a.txt</a> 02-Jan-2024 10:11 100\n<a href=\"b.txt\">b.txt</a> 02-Jan-2024 10:11 200\n")]
	[InlineData("<html><body><pre></pre><a href=\"a.txt\">a.txt</a></body></html>")]
	public async Task OddPages_GiveTheSameResultBothWays(string html)
	{
		await AssertSameBothWays(html, html);
	}

	[Theory]
	[InlineData("<pre><a href=\"a\">a</a></pre>", true)]
	[InlineData("<pre class=\"x\"><a href=\"a\">a</a></pre>", false)]
	[InlineData("<pre><a href=\"a\">a</a></pre><pre><a href=\"b\">b</a></pre>", false)]
	[InlineData("<pre><a href=\"a\">a</a>", false)]
	[InlineData("</pre><pre><a href=\"a\">a</a></pre>", false)]
	[InlineData("<pre>text &amp; more</pre>", false)]
	[InlineData("<pre>carriage\r\nreturn</pre>", true)]
	[InlineData("<pre><a href=\"a\" id=\"x\">a</a></pre>", false)]
	[InlineData("<pre><b>bold</b></pre>", false)]
	[InlineData("<pre><a href='a'>a</a></pre>", false)]
	public void TryExtract_OnlyAcceptsTheStrictShape(string html, bool accepted)
	{
		Assert.Equal(accepted, RawPreListing.TryExtract(html) is not null);
	}

	[Fact]
	public void TryExtract_SplitsLikeTheDomWould()
	{
		RawPreListing.Extracted extracted = RawPreListing.TryExtract("<html><body><pre>\n<a href=\"a\">a</a>\n\n<hr><a href=\"b\">b</a> x\n</pre><p>after</p></body></html>");

		Assert.NotNull(extracted);
		Assert.Equal(["<a href=\"a\">a</a>", "", "", "<a href=\"b\">b</a> x", ""], extracted.Lines);
		Assert.Equal($"<html><body><pre {RawPreListing.MarkerAttribute}=\"1\"></pre><p>after</p></body></html>", extracted.HtmlWithMarkedPre);
	}

	[Fact]
	public void ThePageIsStillParsedTheNormalWay_WhenTheDefaultThresholdIsNotReached()
	{
		RawPreListing.MinimumLength = RawPreListing.DefaultMinimumLength;

		Assert.Equal(200_000, RawPreListing.MinimumLength);
	}
}
