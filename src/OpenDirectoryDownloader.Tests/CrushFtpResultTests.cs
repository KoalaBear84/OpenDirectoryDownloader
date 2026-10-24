using OpenDirectoryDownloader.Parsing.Sites.CrushFtp;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>Some CrushFTP servers send every number as a string, others send numbers; a name can hold an unescaped backslash.</summary>
public class CrushFtpResultTests
{
	[Fact]
	public void FromJson_ReadsNumbersSentAsStrings()
	{
		const string json =
			"""
			{
				"privs" : "(read)(view)(resume)",
				"path" : "/Root/",
				"bytes_sent" : "0",
				"max_upload_amount_available" : "0",
				"listing" : [{
			"name" : "archive.7z",
			"type" : "FILE",
			"href_path" : "/archive.7z",
			"size" : "11532182",
			"num_items" : "1",
			"preview" : "0"
			}
			]
			}
			""";

		CrushFtpResult result = CrushFtpResult.FromJson(json);

		Assert.Equal(0, result.BytesSent);
		Assert.Single(result.Listing);
		Assert.Equal(11532182, result.Listing[0].Size);
		Assert.Equal("/archive.7z", result.Listing[0].HrefPath);
	}

	[Fact]
	public void FromJson_ReadsNumbersSentAsNumbers()
	{
		const string json =
			"""
			{
				"path" : "/",
				"bytes_sent" : 5,
				"listing" : [{ "name" : "a.txt", "type" : "FILE", "href_path" : "/a.txt", "size" : 81 }]
			}
			""";

		CrushFtpResult result = CrushFtpResult.FromJson(json);

		Assert.Equal(5, result.BytesSent);
		Assert.Equal(81, result.Listing[0].Size);
	}

	[Fact]
	public void FromJson_ReadsANameWithAnUnescapedBackslash()
	{
		const string json =
			"""
			{
				"path" : "/Root/",
				"listing" : [{ "name" : "Dir\Cfile.txt", "type" : "FILE", "href_path" : "/Dir\Cfile.txt", "size" : "10" }]
			}
			""";

		CrushFtpResult result = CrushFtpResult.FromJson(json);

		Assert.Equal(@"Dir\Cfile.txt", result.Listing[0].Name);
		Assert.Equal(@"/Dir\Cfile.txt", result.Listing[0].HrefPath);
	}

	[Fact]
	public void EscapeLooseBackslashes_KeepsValidEscapes()
	{
		Assert.Equal(@"{""a"":""x\\y \n é \"" \/""}", CrushFtpResult.EscapeLooseBackslashes(@"{""a"":""x\y \n é \"" \/""}"));
	}

	[Theory]
	[InlineData("<title>CrushFTP - WebInterface</title><script src=\"/WebInterface/custom.js\"></script>", true)]
	[InlineData("<title>Index of /</title><a href=\"file.txt\">file.txt</a>", false)]
	[InlineData("<title>About CrushFTP</title>", false)]
	[InlineData(null, false)]
	public void IsWebInterface_RecognizesTheLoginPage(string html, bool expected)
	{
		Assert.Equal(expected, CrushFtpParser.IsWebInterface(html));
	}

	[Theory]
	[InlineData("https://example.org/", "/")]
	[InlineData("https://example.org/Some%20Dir/Sub/", "/Some Dir/Sub/")]
	[InlineData("https://example.org/#/Some%20Dir/Sub/", "/Some Dir/Sub/")]
	[InlineData("https://example.org/#/Dir%23One/", "/Dir#One/")]
	[InlineData("https://example.org/Other/#/Some%20Dir/", "/Some Dir/")]
	public void GetListingPath_UsesTheFragmentOfTheWebInterface(string url, string expected)
	{
		Assert.Equal(expected, CrushFtpParser.GetListingPath(new Uri(url)));
	}
}
