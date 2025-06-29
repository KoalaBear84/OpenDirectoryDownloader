using OpenDirectoryDownloader.Shared.Models;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class DirectoryParser076_100Tests : DirectoryParserTests
{
	/// <summary>
	/// Url: http://www.colladomusical.com/Music/index.php?dir=Musica/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing75aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(102, webDirectory.Subdirectories.Count);
		Assert.Equal("Abril 2012", webDirectory.Subdirectories[0].Name);
		Assert.Equal(6, webDirectory.Files.Count);
		Assert.Equal("BONCHEDIGITAL.COM_5002673.apk", webDirectory.Files[0].FileName);
		Assert.Equal(5976883, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://www.colladomusical.com/Music/index.php?dir=Musica/Abril%202012/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing75bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(3, webDirectory.Subdirectories.Count);
		Assert.Equal("17-4-2012", webDirectory.Subdirectories[0].Name);
		Assert.Equal(17, webDirectory.Files.Count);
		Assert.Equal("Alex Bueno - Mi Razon %28Tema New 2012%29.mp3", webDirectory.Files[0].FileName);
		Assert.Equal(3565158, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://1fichier.com/dir/NP71kSmd/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing76aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), checkParents: false);

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(29, webDirectory.Files.Count);
		Assert.Equal("Cosmos.A.Space.Time.Odyssey.S01E01.480p.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(158198661, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://jonestown.sdsu.edu/?page_id=29043
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing77aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), checkParents: false);

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(487, webDirectory.Files.Count);
		Assert.Equal("Q836 (Side A).mp3", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://www.sec.gov/Archives/edgar/data/19617
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing78aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(4, webDirectory.Subdirectories.Count);
		Assert.Equal("000089109219002813", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://www.sec.gov/Archives/edgar/data/19617/000089109219002813
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing78bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(3, webDirectory.Subdirectories.Count);
		// Yes, very strange directory listing
		Assert.Equal("0000891092-19-002813-index-headers.html", webDirectory.Subdirectories[0].Name);
		Assert.Equal(5, webDirectory.Files.Count);
		Assert.Equal("e4343_424b2.htm", webDirectory.Files[0].FileName);
		Assert.Equal(84285, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://static.netsyms.net/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing79aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(6, webDirectory.Subdirectories.Count);
		Assert.Equal("bootstrap", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://static.netsyms.net/bootstrap/3/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing79bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(18, webDirectory.Files.Count);
		Assert.Equal("bootstrap.cerulean.min.css", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://esheavyindustries.com/zines/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing80aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(221, webDirectory.Subdirectories.Count);
		Assert.Equal("020", webDirectory.Subdirectories[0].Name);
		Assert.Single(webDirectory.Files);
		Assert.Equal("publish.txt", webDirectory.Files[0].FileName);
		Assert.Equal(24576, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://esheavyindustries.com/zines/020/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing80bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("020_1.txt", webDirectory.Files[0].FileName);
		Assert.Equal(28672, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://mephistope.homegnu.org/docu/music/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing81aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(16, webDirectory.Subdirectories.Count);
		Assert.Equal("n", webDirectory.Subdirectories[0].Name);
		Assert.Equal(67, webDirectory.Files.Count);
		Assert.Equal("396Hz - Liberation From Fear (Solfeggio Tones).failed-conv.mp4", webDirectory.Files[0].FileName);
		Assert.Equal(7172260, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://mlpeps.com/mlp/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing82aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(12, webDirectory.Subdirectories.Count);
		Assert.Equal("Equestria.Girls", webDirectory.Subdirectories[0].Name);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("Game.of.Thrones.S08E01.Kings.Landing.720p.AMZN.WEB-DL.DDP5.1.H.264-GoT.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(1073741824, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://mlpeps.com/mlp/Season.9/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing82bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("720p", webDirectory.Subdirectories[0].Name);
		Assert.Equal(5, webDirectory.Files.Count);
		Assert.Equal("My.Little.Pony.Friendship.is.Magic.S09E01.The.Beginning.of.the.End.Part.1.1080p.iT.WEB-DL.DD5.1.H.264-iT00NZ.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(904921088, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://cybercloud.zone/dirs/movie/movieland/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing83aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), checkParents: false);

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(643, webDirectory.Files.Count);
		Assert.Equal("Stan.and.Ollie.2018.BDRip.x264-DRONES.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(524288000, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://webmail.rabouin.es/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing84aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("Claustrophobia", webDirectory.Subdirectories[0].Name);
		Assert.Equal(4, webDirectory.Files.Count);
		Assert.Equal("bonnie++", webDirectory.Files[0].FileName);
		Assert.Equal(55296, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://webmail.rabouin.es/Claustrophobia/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing84bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(21, webDirectory.Files.Count);
		Assert.Equal("Air putride.pdf", webDirectory.Files[0].FileName);
		Assert.Equal(4458496, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://support.j2rs.com/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing85aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(9, webDirectory.Subdirectories.Count);
		Assert.Equal("225_625", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://support.j2rs.com/?directory=.%2FDocuments%2F
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing85bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(7, webDirectory.Subdirectories.Count);
		Assert.Equal("EnglishDocs", webDirectory.Subdirectories[0].Name);
		Assert.Equal(4, webDirectory.Files.Count);
		Assert.Equal("J2 Products Comparison.pdf", webDirectory.Files[0].FileName);
		Assert.Equal(72704, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://103.81.104.98/DATA/index.php?dir=NAS1
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing86aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(10, webDirectory.Subdirectories.Count);
		Assert.Equal("Bangladeshi-Natok-UP", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://103.81.104.98/DATA/index.php?dir=NAS1/Bangladeshi-Natok-UP/Binodoni%20Kinba%20Kobi/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing86bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("Bangla New_Latest Natok March 2013 - Binodoni Kinba Kobi %28HQ%29 by - %5BMim%2CMilon%5D-thumb.jpg", webDirectory.Files[0].FileName);
		Assert.Equal(21094, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://thiscatis.online/shared/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing87aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(6, webDirectory.Subdirectories.Count);
		Assert.Equal("Audio", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://thiscatis.online/shared/Books/Academic%20Papers/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing87bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(32, webDirectory.Files.Count);
		Assert.Equal("10.0000@newleftreview.org@3150.pdf", webDirectory.Files[0].FileName);
		Assert.Equal(665600, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://193.68.19.127/AZBUCHNIK/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing88aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(22, webDirectory.Subdirectories.Count);
		Assert.Equal("A", webDirectory.Subdirectories[0].Name);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("Radio.pls", webDirectory.Files[0].FileName);
		Assert.Equal(3277, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://193.68.19.127/AZBUCHNIK/A/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing88bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(54, webDirectory.Subdirectories.Count);
		Assert.Equal("ABID SAKALAS-2005", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://www.otakucloud.net/A:/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing89aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(5, webDirectory.Subdirectories.Count);
		Assert.Equal("写真", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://www.otakucloud.net/A:/%E5%86%99%E7%9C%9F/%E8%90%9D%E8%8E%89%E6%B6%B2%E6%B6%B2%E9%85%B1YeYe/01%E6%B6%B2%E6%B6%B2%E9%85%B1%E2%80%94%E5%BC%B9%E4%B8%B8%E8%AE%BA%E7%A0%B4%E8%8E%AB%E8%AF%BA%E7%BE%8E%E6%8B%9F%E4%BA%BAcos%20%2832P%2B3V%29
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing89bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Single(webDirectory.Subdirectories);
		Assert.Equal("视频", webDirectory.Subdirectories[0].Name);
		Assert.Equal(32, webDirectory.Files.Count);
		Assert.Equal("␠(1).jpg", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://www.otakucloud.net/B:
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing89cAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(3, webDirectory.Subdirectories.Count);
		Assert.Equal("本子", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://1-up.cc/roms/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing90aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(36, webDirectory.Subdirectories.Count);
		Assert.Equal("Amiga", webDirectory.Subdirectories[0].Name);
		Assert.Single(webDirectory.Files);
		Assert.Equal("[All Systems-Complete].torrent", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://1-up.cc/roms/Amiga/0/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing90bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(8, webDirectory.Files.Count);
		Assert.Equal("3D Boxing.zip", webDirectory.Files[0].FileName);
		Assert.Null(webDirectory.Files[0].FileSize);
	}

	// TODO:
	/// <summary>
	/// Url: https://repo1.maven.org/maven2/HTTPClient/HTTPClient/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing91aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Single(webDirectory.Subdirectories);
		Assert.Equal("0.3-3", webDirectory.Subdirectories[0].Name);
		Assert.Equal(3, webDirectory.Files.Count);
		Assert.Equal("maven-metadata.xml", webDirectory.Files[0].FileName);
		Assert.Equal(212, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://repo1.maven.org/maven2/HTTPClient/HTTPClient/0.3-3/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing91bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(9, webDirectory.Files.Count);
		Assert.Equal("HTTPClient-0.3-3.jar", webDirectory.Files[0].FileName);
		Assert.Equal(733741, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://repo1.maven.org/maven2/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing91cAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(124, webDirectory.Subdirectories.Count);
		Assert.Equal("HTTPClient", webDirectory.Subdirectories[0].Name);
		Assert.Equal(6, webDirectory.Files.Count);
		Assert.Equal("archetype-catalog.xml", webDirectory.Files[0].FileName);
		Assert.Equal(9064608, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://archive.koba.li/?dir=ACS
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing92aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("Season 1", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://archive.koba.li/?dir=ACS/Season%201
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing92bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(60, webDirectory.Files.Count);
		Assert.Equal("American Crime Story S01E01 From the Ashes of Tragedy-thumb.jpg", webDirectory.Files[0].FileName);
		Assert.Equal(90184, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://archive.midnightchannel.net/SonyPS/PS3/Source%20Code/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing93aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("Scripts", webDirectory.Subdirectories[0].Name);
		Assert.Single(webDirectory.Files);
		Assert.Equal("aldostools-ps3netsrv-20170310-a61f9354a2d122e92f9336ac6f110d911ad0e629.tar.gz", webDirectory.Files[0].FileName);
		Assert.Equal(27341, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://archive.midnightchannel.net/zefie/linux/nexus7_2012/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing93bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Single(webDirectory.Subdirectories);
		Assert.Equal("ubuntu-13.04", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://archive.midnightchannel.net/SonyPS/PS3/Source Code/gitorious.ps3dev.net/
	/// Added for specific check regarding ".net" which looks like an extension
	/// But fixed in another way, outside of the ParseHtml
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing93cAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample(), "https://archive.midnightchannel.net/SonyPS/PS3/Source Code/gitorious.ps3dev.net/");

		Assert.Equal("gitorious.ps3dev.net", webDirectory.Name);
		Assert.Equal(44, webDirectory.Subdirectories.Count);
		Assert.Equal("4.40, 4.41, 4.46 and 4.50 PS3 Key", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://pacsteam.org/Shareware/Games/lost-coast/
	/// Added to check for empty listing
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing94aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://dl.jeremylee.sh/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing95aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(3, webDirectory.Subdirectories.Count);
		Assert.Equal("DEF CON 1", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://dl.jeremylee.sh/Videos/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing95bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(28, webDirectory.Subdirectories.Count);
		Assert.Equal("2005 - Avatar The Last Airbender S01-S03", webDirectory.Subdirectories[0].Name);
		Assert.Equal(20, webDirectory.Files.Count);
		Assert.Equal("1980 - The Final Countdown.mkv", webDirectory.Files[0].FileName);
		Assert.Equal(1370035176, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: https://home.bjeanes.com/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing96aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(6, webDirectory.Subdirectories.Count);
		Assert.Equal("Climbing", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://home.bjeanes.com/Other%20Videos/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing96bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("@eaDir", webDirectory.Subdirectories[0].Name);
		Assert.Equal(4, webDirectory.Files.Count);
		Assert.Equal("Uncharted Lines (2017).mp4", webDirectory.Files[0].FileName);
		Assert.Equal(2469606195, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://ftp.uni-bayreuth.de/netsoftware/apache/trafficserver/patches/
	/// Added to check for empty listing
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing97aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://82.94.215.218/download/
	/// Added to check "Input string was not in a correct format."
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing98aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(27, webDirectory.Subdirectories.Count);
		Assert.Equal("Dj Vela", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: http://82.94.215.218/download/Dj%20Vela/
	/// Added to check "Input string was not in a correct format."
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing98bAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(2, webDirectory.Subdirectories.Count);
		Assert.Equal("short promo mixes", webDirectory.Subdirectories[0].Name);
		Assert.Equal(8, webDirectory.Files.Count);
		Assert.Equal("DJ VELA & ROLLIN THUNDER 20.9.2014 in Tresor .mp3", webDirectory.Files[0].FileName);
		Assert.Equal(288358400, webDirectory.Files[0].FileSize);
	}

	/// <summary>
	/// Url: http://ftp.heanet.ie/mirrors/ubuntu-cdimage/source/source/
	/// Added to check for sortable headers on multiple properties
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing99aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Equal(6, webDirectory.Subdirectories.Count);
		Assert.Equal("20200403", webDirectory.Subdirectories[0].Name);
		Assert.Empty(webDirectory.Files);
	}

	/// <summary>
	/// Url: https://archive.midnightchannel.net/SonyPS/Firmware/
	/// </summary>
	[Fact]
	public async Task TestDirectoryListing100aAsync()
	{
		WebDirectory webDirectory = await ParseHtml(GetSample());

		Assert.Equal("ROOT", webDirectory.Name);
		Assert.Empty(webDirectory.Subdirectories);
		Assert.Equal(149, webDirectory.Files.Count);
		Assert.Equal("CEX_CRC[37b6628d]_FW[v1.02]_PS3UPDAT.PUP", webDirectory.Files[0].FileName);
		Assert.Equal(95357501, webDirectory.Files[0].FileSize);
	}
}
