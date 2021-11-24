using OpenDirectoryDownloader.Shared.Models;
using System.Threading.Tasks;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class DirectoryParser126_150Tests : DirectoryParserTests
{
	/// <summary>
	/// Url: http://dl.birseda.net/Archive/index.php?dir=K
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing126aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(210, webDirectory.Subdirectories.Count);
		Assert.Equal("Kako Band", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://dl.birseda.net/Archive/index.php?dir=K%2FKako%20Band%2FSingle%2F128
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing126bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(2, webDirectory.Files.Count);
		Assert.Equal("Kako Band - Paradise [128].mp3", webDirectory.Files[0].FileName);
		Assert.Equal(3022848, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://amogus.uk/public/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing127aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(115, webDirectory.Subdirectories.Count);
		Assert.Equal("�", webDirectory.Subdirectories[0].Name);
		Assert.Equal(188, webDirectory.Files.Count);
		Assert.Equal("1024x768.jpg", webDirectory.Files[0].FileName);
		Assert.Equal(67072, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://amogus.uk/public/%EF%BF%BD/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing127bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(7, webDirectory.Files.Count);
		Assert.Equal("paint.zip", webDirectory.Files[0].FileName);
		Assert.Equal(5530, webDirectory.Files[0].FileSize);
	}
}
