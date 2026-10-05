using System.Net;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class GetFileNameFromUrlTests
{
	private static string Old(string fullUrl) => Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath));

	private static void AssertSame(string baseUrl, string href)
	{
		Library.ProcessUrl(baseUrl, href, out _, out Uri uri, out string fullUrl);

		Assert.True(Old(fullUrl) == Library.GetFileNameFromUrl(uri, fullUrl), $"Different file name for href '{href}' on '{baseUrl}'");
	}

	[Theory]
	[InlineData("file-00042.mkv")]
	[InlineData("a b.txt")]
	[InlineData("a%20b.txt")]
	[InlineData("a+b.txt")]
	[InlineData("a%2Bb.txt")]
	[InlineData("%78.txt")]
	[InlineData("%25x.txt")]
	[InlineData("%2541.txt")]
	[InlineData("café.txt")]
	[InlineData("caf%C3%A9.txt")]
	[InlineData("中文.txt")]
	[InlineData("sub/dir/file.txt")]
	[InlineData("sub%2Fdir.txt")]
	[InlineData("../up/file.txt")]
	[InlineData("./same.txt")]
	[InlineData("file.txt?x=1")]
	[InlineData("file.txt#frag")]
	[InlineData("dir/")]
	[InlineData("a;b=c,d.txt")]
	[InlineData("'quoted'(brackets)!.txt")]
	[InlineData("back\\slash.txt")]
	[InlineData("{curly}[square]|pipe^caret`tick.txt")]
	public void SameAsTheOldExpression_ForHandPickedHrefs(string href)
	{
		foreach (string baseUrl in new[] { "http://127.0.0.1:8086/", "https://Files.Example.Test/a/b/", "http://h/dir%20x/", "http://h/a/b"})
		{
			AssertSame(baseUrl, href);
		}
	}

	[Fact]
	public void SameAsTheOldExpression_ForRandomHrefs()
	{
		Random random = new(2024);
		string[] baseUrls = ["http://127.0.0.1:8086/", "http://files.example.test/a/b/", "https://Host.Example/dir%20x/", "http://h/q?x=1", "http://h/a/b"];
		string alphabet = "abcXYZ019 ._-+%/\\?#:;=&~!$'()*,@[]{}^`|\"<>é中 ";
		string hex = "0123456789ABCDEFabcdef";
		int compared = 0;

		for (int i = 0; i < 200_000; i++)
		{
			char[] chars = new char[random.Next(1, 14)];

			for (int c = 0; c < chars.Length; c++)
			{
				chars[c] = random.Next(8) == 0 ? '%' : alphabet[random.Next(alphabet.Length)];
			}

			string href = new(chars);

			if (random.Next(3) == 0 && href.Length > 3)
			{
				href = href.Insert(random.Next(href.Length - 2), "%" + hex[random.Next(hex.Length)] + hex[random.Next(hex.Length)]);
			}

			string baseUrl = baseUrls[random.Next(baseUrls.Length)];

			try
			{
				Library.ProcessUrl(baseUrl, href, out _, out Uri uri, out string fullUrl);

				string expected = Old(fullUrl);

				Assert.True(expected == Library.GetFileNameFromUrl(uri, fullUrl), $"Different file name for href '{href}' on '{baseUrl}'");
				compared++;
			}
			catch (UriFormatException)
			{
				// Not a valid URL in the first place: nothing to name
			}
		}

		Assert.True(compared > 100_000, $"Only {compared} usable hrefs");
	}
}
