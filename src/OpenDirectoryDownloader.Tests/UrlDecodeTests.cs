using System.Text;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class UrlDecodeTests
{
	/// <summary>
	/// Test URL decoding with UTF-8 encoding (default)
	/// </summary>
	[Fact]
	public void TestUrlDecode_UTF8()
	{
		// UTF-8 encoding of "é" is %C3%A9
		string encoded = "pr%C3%A9-montage.aiff";
		string expected = "pré-montage.aiff";
		
		string result = Library.UrlDecode(encoded);
		
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Test URL decoding with ISO-8859-1 encoding (Latin-1)
	/// </summary>
	[Fact]
	public void TestUrlDecode_ISO88591()
	{
		// ISO-8859-1 encoding of "é" is %E9
		string encoded = "pr%e9-montage.aiff";
		string expected = "pré-montage.aiff";
		
		Encoding iso88591 = Encoding.GetEncoding("ISO-8859-1");
		string result = Library.UrlDecode(encoded, iso88591);
		
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Test URL decoding with Windows-1252 encoding
	/// </summary>
	[Fact]
	public void TestUrlDecode_Windows1252()
	{
		// Windows-1252 encoding of "é" is %E9 (same as ISO-8859-1 for this character)
		string encoded = "pr%e9-montage.aiff";
		string expected = "pré-montage.aiff";
		
		Encoding windows1252 = Encoding.GetEncoding("Windows-1252");
		string result = Library.UrlDecode(encoded, windows1252);
		
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Test URL decoding with plus signs representing spaces
	/// </summary>
	[Fact]
	public void TestUrlDecode_PlusSigns()
	{
		string encoded = "my+file+name.txt";
		string expected = "my file name.txt";
		
		string result = Library.UrlDecode(encoded);
		
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Test URL decoding with mixed content
	/// </summary>
	[Fact]
	public void TestUrlDecode_MixedContent()
	{
		// Mix of regular characters, encoded characters, and spaces
		string encoded = "file+with%20spaces+and+%C3%A9.txt";
		string expected = "file with spaces and é.txt";
		
		string result = Library.UrlDecode(encoded);
		
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Test URL decoding with null or empty string
	/// </summary>
	[Fact]
	public void TestUrlDecode_NullOrEmpty()
	{
		Assert.Null(Library.UrlDecode(null));
		Assert.Equal("", Library.UrlDecode(""));
		Assert.Equal("   ", Library.UrlDecode("   "));
	}

	/// <summary>
	/// Test URL decoding with no encoded characters
	/// </summary>
	[Fact]
	public void TestUrlDecode_NoEncoding()
	{
		string input = "simple-filename.txt";
		string expected = "simple-filename.txt";
		
		string result = Library.UrlDecode(input);
		
		Assert.Equal(expected, result);
	}

	/// <summary>
	/// Test URL decoding with multiple encoded characters in ISO-8859-1
	/// </summary>
	[Fact]
	public void TestUrlDecode_MultipleCharacters_ISO88591()
	{
		// ISO-8859-1: è=%E8, é=%E9, à=%E0
		string encoded = "caf%e9+du+gar%e7on.txt";
		string expected = "café du garçon.txt";
		
		Encoding iso88591 = Encoding.GetEncoding("ISO-8859-1");
		string result = Library.UrlDecode(encoded, iso88591);
		
		Assert.Equal(expected, result);
	}
}
