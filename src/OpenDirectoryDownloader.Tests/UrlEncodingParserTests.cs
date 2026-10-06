using System.Text.RegularExpressions;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// UrlEncodingParser was a NameValueCollection, created for nearly every link. The lean version must answer exactly
/// like it did (case-insensitive keys, raw values escaped again by ToString, splitting without a '?'): the
/// listing parsers decide on its answers. Compared on every link of every sample page and some odd ones.
/// </summary>
public partial class UrlEncodingParserTests
{
	private static readonly string[] Keys = ["dir", "DIR", "path", "file", "folder", "url", "download", "token", "sortby", "dirname", "directory", "C", "O", "sort", "order", "id", "resourcekey", "usp", "missing"];

	private static void AssertSame(string input)
	{
		ReferenceUrlEncodingParser expected = new(input);
		UrlEncodingParser actual = new(input);

		Assert.Equal(expected.Count, actual.Count);
		Assert.Equal(expected.AllKeys, actual.AllKeys);
		Assert.Equal(expected.ToString(), actual.ToString());

		foreach (string key in Keys)
		{
			Assert.Equal(expected[key], actual[key]);
			Assert.Equal(expected.Get(key), actual.Get(key));
		}

		// What StripUrl and the token clean up do
		foreach (string key in new[] { "C", "token", "sort", expected.AllKeys.FirstOrDefault() ?? "x" })
		{
			expected.Remove(key);
			actual.Remove(key);

			Assert.Equal(expected.Count, actual.Count);
			Assert.Equal(expected.ToString(), actual.ToString());
		}
	}

	[Theory]
	[InlineData("http://example.com/")]
	[InlineData("http://example.com/?dir=a%20b&C=N;O=D")]
	[InlineData("http://example.com/?DIR=x&dir=y&Dir=z")]
	[InlineData("http://example.com/index.php?dir=a/b&file=c.txt&token=abc")]
	[InlineData("http://example.com/a=b/c")]
	[InlineData("http://example.com/a=b&c=d")]
	[InlineData("?x=1&&y=&=z&w")]
	[InlineData("file.zip?token=t1&x=%41")]
	[InlineData("http://example.com/?sort=name&order=asc")]
	[InlineData("http://example.com/?a=1?b=2")]
	[InlineData("")]
	[InlineData("plain")]
	public void Odd_Inputs(string input) => AssertSame(input);

	[Fact]
	public void EveryLinkOfEverySample()
	{
		int links = 0;

		foreach (string path in Directory.EnumerateFiles("Samples", "*.dat"))
		{
			foreach (Match match in HrefRegex().Matches(File.ReadAllText(path)))
			{
				string href = System.Net.WebUtility.HtmlDecode(match.Groups["Href"].Value);

				AssertSame(href);

				if (Uri.TryCreate(new Uri("http://localhost/listing/"), href, out Uri uri))
				{
					AssertSame(uri.ToString());
				}

				links++;
			}
		}

		Assert.True(links > 10_000, $"Expected the links of the whole sample corpus, found {links}");
	}

	[GeneratedRegex(@"href\s*=\s*[""'](?<Href>[^""']*)[""']", RegexOptions.IgnoreCase)]
	private static partial Regex HrefRegex();
}
