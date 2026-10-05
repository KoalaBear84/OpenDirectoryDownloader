using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// OpenDirectoryIndexer.GetHtmlString decides whether a response body is HTML and returns its text. It replaced a
/// pipeline that decoded the body, re-encoded it into a MemoryStream and decoded it again; these pin down what
/// callers rely on, so the single decode has to give the same answers.
/// </summary>
public class GetHtmlStringTests
{
	private static readonly MethodInfo GetHtmlString = typeof(OpenDirectoryIndexer).GetMethod("GetHtmlString", BindingFlags.NonPublic | BindingFlags.Static)!;

	private static async Task<string> Read(byte[] body, string charSet = null, bool lieAboutLength = false)
	{
		using HttpResponseMessage response = new() { Content = new ByteArrayContent(body) };
		response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = charSet };

		if (lieAboutLength)
		{
			response.Content.Headers.ContentLength = 1_000_000_000;
		}

		return await (Task<string>)GetHtmlString.Invoke(null, [response])!;
	}

	private const string Page = "<html><head><title>Index of /</title></head><body><a href=\"movie.mkv\">movie.mkv</a> café über</body></html>";

	[Fact]
	public async Task Utf8Html_IsReturnedAsIs() => Assert.Equal(Page, await Read(Encoding.UTF8.GetBytes(Page)));

	[Fact]
	public async Task Utf8Bom_IsStripped()
	{
		byte[] body = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Page)];

		Assert.Equal(Page, await Read(body));
	}

	[Fact]
	public async Task LegacyCharset_IsDecodedWithThatCharset()
	{
		Encoding latin1 = Encoding.GetEncoding("iso-8859-1");

		Assert.Equal(Page, await Read(latin1.GetBytes(Page), "iso-8859-1"));
	}

	[Fact]
	public async Task Utf16WithBom_IsDecodedLikeBefore()
	{
		byte[] body = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Page)];

		Assert.Equal(Page, await Read(body));
	}

	[Fact]
	public async Task EmptyBody_GivesAnEmptyStringNotNull() => Assert.Equal(string.Empty, await Read([]));

	[Fact]
	public async Task NotHtml_GivesNull()
	{
		Assert.Null(await Read(Encoding.UTF8.GetBytes("just plain text, no tags at all")));
		Assert.Null(await Read(Enumerable.Range(0, 5_000).Select(i => (byte)(i % 7)).ToArray()));
		Assert.Null(await Read(Encoding.UTF8.GetBytes("<>")));
	}

	[Fact]
	public async Task BigBody_IsReturnedCompletely_EvenWithAWrongContentLength()
	{
		StringBuilder html = new("<html><body><pre>");

		for (int i = 0; i < 150_000; i++)
		{
			html.Append("<a href=\"file-").Append(i).Append(".mkv\">file-").Append(i).Append(".mkv</a> é\n");
		}

		html.Append("</pre></body></html>");
		string expected = html.ToString();
		byte[] body = Encoding.UTF8.GetBytes(expected);

		Assert.Equal(expected, await Read(body));
		Assert.Equal(expected, await Read(body, lieAboutLength: true));
	}

	[Fact]
	public async Task BodyOfExactlyTheCheckedPrefix_IsReturnedCompletely()
	{
		// 10 KB is the size of the up-front HTML check; a body that ends exactly there must not lose or repeat anything
		string html = "<p id=\"a\">" + new string('x', 10 * 1024 - 10);

		Assert.Equal(10 * 1024, html.Length);
		Assert.Equal(html, await Read(Encoding.UTF8.GetBytes(html)));
		Assert.Equal(html + "y", await Read(Encoding.UTF8.GetBytes(html + "y")));
	}
}
