using OpenDirectoryDownloader.Shared.Models;
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

	/// <summary>
	/// Url: http://www.manuals.cornpone.net/
	/// https://sourceforge.net/projects/dir-list/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing128aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(7, webDirectory.Subdirectories.Count);
		Assert.Equal(".well-known", webDirectory.Subdirectories[0].Name);
		Assert.Single(webDirectory.Files);
		Assert.Equal("robots.txt", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://www.manuals.cornpone.net/index.php?folder=Q3J5c3RhbCBSYWRpbw==
	/// https://sourceforge.net/projects/dir-list/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing128bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(28, webDirectory.Files.Count);
		Assert.Equal("101+ Crystal Radio Circuits.pdf", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://104.168.151.193/
	/// New plain text format
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing129aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Single(webDirectory.Subdirectories);
		Assert.Equal("ghostpdl", webDirectory.Subdirectories[0].Name);
		Assert.Equal(9, webDirectory.Files.Count);
		Assert.Equal("Easy Effortless Fix for Your Dowagers Hump!-A_hSiiLtKeU.mp4", webDirectory.Files[0].FileName);
		Assert.Equal(14680064, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://104.168.151.193/ghostpdl/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing129bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(31, webDirectory.Subdirectories.Count);
		Assert.Equal(".git", webDirectory.Subdirectories[0].Name);
		Assert.Equal(8, webDirectory.Files.Count);
		Assert.Equal(".gitattributes", webDirectory.Files[0].FileName);
		Assert.Equal(486, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://78.203.154.250/
	/// New plain text format
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing130aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(310, webDirectory.Subdirectories.Count);
		Assert.Equal("30.Rock.iNTEGRALE.FRENCH.WEB.H264-FRATERNiTY", webDirectory.Subdirectories[0].Name);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("Interstellar.2014.TRUEFRENCH.BRRip.x264.AC3-SVR.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(1437711157, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://78.203.154.250/30.Rock.iNTEGRALE.FRENCH.WEB.H264-FRATERNiTY/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing130bAsync()
	{
		// TODO
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(139, webDirectory.Files.Count);
		Assert.Equal("30.Rock.S01E01.FRENCH.WEB.H264-FRATERNiTY.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(183872151, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://dl.malwarewatch.org/
	/// Colspan on both thead and tbody
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing131aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(6, webDirectory.Subdirectories.Count);
		Assert.Equal("drivers", webDirectory.Subdirectories[0].Name);
		Assert.Single(webDirectory.Files);
		Assert.Equal("softwarelisting.xlsx", webDirectory.Files[0].FileName);
		Assert.Equal(19251, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://dl.malwarewatch.org/software/advanced/remote-tools/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing131bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(8, webDirectory.Files.Count);
		Assert.Equal("Remote Tools 2015 (x64).zip", webDirectory.Files[0].FileName);
		Assert.Equal(62285414, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://ftp.lindin.fo/FTP%20arkiv/
	/// HFS 2.3x
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing132aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(36, webDirectory.Subdirectories.Count);
		Assert.Equal("7 ar - Sendingar ur savninum hja Lindini", webDirectory.Subdirectories[0].Name);
		Assert.Single(webDirectory.Files);
		Assert.Equal("Fragreiding um at Downloada sendingar.txt", webDirectory.Files[0].FileName);
		Assert.Equal(1536, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://ftp.lindin.fo/FTP%20arkiv/7%20ar%20-%20Sendingar%20ur%20savninum%20hja%20Lindini/
	/// HFS 2.3x
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing132bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(7, webDirectory.Files.Count);
		Assert.Equal("7ar_bonarlinjan_001123_Ellen Jorgen Mohlin.mp3", webDirectory.Files[0].FileName);
		Assert.Equal(34812723, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://localhost:8885/ODTest/
	/// HFS 2.4x
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing133aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(4, webDirectory.Subdirectories.Count);
		Assert.Equal("¡¢£¤¥ăĄƃɔʵ̆΅ЄԆԷ اܑऌঋਕએଓஒఎಐഎඐทພ༩ဒႱᄎሓᎷᐖᚖᚸᜃភᠮℕⅨ↦", webDirectory.Subdirectories[0].Name);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("∌⌀②▄▣☂✄⠅⿴〄.test", webDirectory.Files[0].FileName);
		Assert.Equal(18637, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://localhost:8885/ODTest/EmptyFolder/
	/// HFS 2.4x
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing133bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://localhost:8000/ODTest/EmptyFolder/
	/// https://github.com/TheWaWaR/simple-http-server/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing134aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(4, webDirectory.Subdirectories.Count);
		Assert.Equal("¡¢£¤¥ăĄƃɔʵ̆΅ЄԆԷ اܑऌঋਕએଓஒఎಐഎඐทພ༩ဒႱᄎሓᎷᐖᚖᚸᜃភᠮℕⅨ↦", webDirectory.Subdirectories[0].Name);
		Assert.Equal(2, webDirectory.Files.Count);
		Assert.Equal("∌⌀②▄▣☂✄⠅⿴〄.test", webDirectory.Files[0].FileName);
		Assert.Equal(19057, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://localhost:8000/ODTest/EmptyFolder/
	/// https://github.com/TheWaWaR/simple-http-server/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing134bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), "http://localhost:8000/ODTest/EmptyFolder/");

		Assert.Equal("EmptyFolder", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://filedn.com/lgm4rog8XwDbvwRIvGBXqry/
	/// https://www.pcloud.com/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing135aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), "https://filedn.com/lgm4rog8XwDbvwRIvGBXqry/");

		Assert.Equal(21, webDirectory.Subdirectories.Count);
		Assert.Equal("***Textbooks", webDirectory.Subdirectories[0].Name);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("First Aid for the USMLE Step 1 2024.pdf", webDirectory.Files[0].FileName);
		Assert.Equal(342792216, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://filedn.com/lgm4rog8XwDbvwRIvGBXqry/%2ABoard%20Review%20Series%20%28BRS%29%20Books/EPUB/
	/// https://www.pcloud.com/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing135bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), "https://filedn.com/lgm4rog8XwDbvwRIvGBXqry/%2ABoard%20Review%20Series%20%28BRS%29%20Books/EPUB/");

		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(5, webDirectory.Files.Count);
		Assert.Equal("BRS Behavioral Science 8e.epub", webDirectory.Files[0].FileName);
		Assert.Equal(11151639, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://localhost:5114/data/
	/// https://github.com/sigoden/dufs
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing136aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("EmptyFolder", webDirectory.Subdirectories[0].Name);
		Assert.Equal(2, webDirectory.Files.Count);
		Assert.Equal("asciifilename.txt", webDirectory.Files[0].FileName);
		Assert.Equal(4, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://localhost:5114/data/EmptyFolder/
	/// https://github.com/sigoden/dufs
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing136bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Empty(webDirectory.Subdirectories);
		Assert.Empty(webDirectory.Files);
	}
}
