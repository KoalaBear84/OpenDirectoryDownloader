using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class DirectoryParser026_050Tests : DirectoryParserTests
    {
        /// <summary>
        /// Url: http://blindleaf.freeservers.com/Tabs/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing26aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(4, webDirectory.Subdirectories.Count);
            Assert.Equal("Bass", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://blindleaf.freeservers.com/Tabs/Bass/112/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing26bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Single(webDirectory.Files);
            Assert.Equal("onlyyou.txt", webDirectory.Files[0].FileName);
            Assert.Equal(875, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ftp.isc.org/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing27aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(8, webDirectory.Subdirectories.Count);
            Assert.Equal("bin", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("ls-lR.gz", webDirectory.Files[0].FileName);
            Assert.Equal(2621440, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ftp.isc.org/isc/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing27bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(25, webDirectory.Subdirectories.Count);
            Assert.Equal("IRRToolSet", webDirectory.Subdirectories[0].Name);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("MIRRORS", webDirectory.Files[0].FileName);
            Assert.Equal(3994, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://reinsweb.ddns.net/scores/index.php
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing28aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(33, webDirectory.Subdirectories.Count);
            Assert.Equal("- blokfluitensemble", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://reinsweb.ddns.net/scores/index.php?path=-+multivocaal/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing28bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("alt-tenor-bas", webDirectory.Subdirectories[0].Name);
            Assert.Equal(12, webDirectory.Files.Count);
            Assert.Equal("bangles-manicmonday.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(102594, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://files.sq10.net/music/vaporwave/list/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing29aAsync()
        {
            Uri uri = new Uri("https://files.sq10.net/music/vaporwave/list/");
            WebDirectory webDirectory = await ParseHtml(GetSample(), uri.ToString());
            CleanWebDirectory(webDirectory, uri);

            Assert.Equal("list", webDirectory.Name);
            Assert.Equal(468, webDirectory.Subdirectories.Count);
            Assert.Equal("$PL▲$H ¢LUB 7-Contact Lens", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://files.sq10.net/music/vaporwave/list/WASTED%20NIGHTS/WASTED%20NIGHTS%20-%20SEAMS%20EP/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing29bAsync()
        {
            Uri uri = new Uri("https://files.sq10.net/music/vaporwave/list/WASTED%20NIGHTS/WASTED%20NIGHTS%20-%20SEAMS%20EP/");
            WebDirectory webDirectory = await ParseHtml(GetSample(), uri.ToString());
            CleanWebDirectory(webDirectory, uri);

            Assert.Equal("WASTED NIGHTS - SEAMS EP", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("WASTED NIGHTS - SEAMS EP - 01 SEAMS.flac", webDirectory.Files[0].FileName);
            Assert.Equal(32715571, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.varmstudio.com/stuff/miisu/William%20Gibson%20Collection/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing30aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("1. Sprawl Trilogy", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://www.varmstudio.com/stuff/miisu/William%20Gibson%20Collection/1.%20Sprawl%20Trilogy/Count%20Zero%20(2231)/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing30bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("Count Zero - William Gibson.epub", webDirectory.Files[0].FileName);
            Assert.Equal(321536, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://104.77.157.44/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing31aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(38, webDirectory.Subdirectories.Count);
            Assert.Equal("CPAN", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://104.77.157.44/antergos/antergos/i686/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing31bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(11, webDirectory.Subdirectories.Count);
            Assert.Equal("_modules", webDirectory.Subdirectories[0].Name);
            Assert.Equal(25, webDirectory.Files.Count);
            Assert.Equal("CPAN.html", webDirectory.Files[0].FileName);
            Assert.Equal(10240, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://104.77.157.44/centos/5.10/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing31cAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://sawiptv.com/media/storage/2017-09-23_GIRONA_V_BARCELONA_FR_2017/
        /// Extra test for empty directory listing
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing32aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://ipfs.io/ipfs/QmSoF8CrdpoorhxWiZiAUBzh3dsSH194NcgLjcNotgGEq7/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing33aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), "http://ipfs.io/");

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(229, webDirectory.Files.Count);
            Assert.Equal("2001_A_Space_Travesty_(2000).CD1.Piratsvin.ShareReactor.avi", webDirectory.Files[0].FileName);
            Assert.Equal(769654784, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://reserve.louie.land/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing34aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("Album Art", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://reserve.louie.land/Ringtones/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing34bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("Zelda", webDirectory.Subdirectories[0].Name);
            Assert.Equal(9, webDirectory.Files.Count);
            Assert.Equal("Jungle Cruise.m4r", webDirectory.Files[0].FileName);
            Assert.Equal(76800, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ocny.oroszwn.com/MP3/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing35aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(35, webDirectory.Subdirectories.Count);
            Assert.Equal("Brain Damage - Out Of The Gord", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("mp3-header.shtml", webDirectory.Files[0].FileName);
            Assert.Equal(3994, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ocny.oroszwn.com/MP3/Brain%20Damage%20-%20Out%20Of%20The%20Gord/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing35bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(8, webDirectory.Files.Count);
            Assert.Equal("Brain Damage - 01.mp3", webDirectory.Files[0].FileName);
            Assert.Equal(2411725, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://93beast.fea.st/files/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing36aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("holybooks", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("Notes to This Distribution.txt", webDirectory.Files[0].FileName);
            Assert.Equal(2048, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://93beast.fea.st/files/section1/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing36bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(26, webDirectory.Subdirectories.Count);
            Assert.Equal("777", webDirectory.Subdirectories[0].Name);
            Assert.Equal(23, webDirectory.Files.Count);
            Assert.Equal("Aphorisms of Patanjali.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(133120, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://s1.bitdl.ir/Software/Development&Programing/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing37aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(97, webDirectory.Subdirectories.Count);
            Assert.Equal("255 of best PHP scripts", webDirectory.Subdirectories[0].Name);
            Assert.Equal(413, webDirectory.Files.Count);
            Assert.Equal("ARDUINO.1.8.8.Portable.bitdownload.ir.rar", webDirectory.Files[0].FileName);
            Assert.Equal(161375846, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://s1.bitdl.ir/Software/Development&Programing/Microsoft.Visual.Studio.2015.Enterprise.Update.2/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing37bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(7, webDirectory.Files.Count);
            Assert.Equal("Microsoft.Visual.Studio.2015.Enterprise.Update.2.www.Download.ir.part1.rar", webDirectory.Files[0].FileName);
            Assert.Equal(1073741824, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Check for https://www.directorylister.com/
        /// Url: http://sr1.animelist1.ir/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing38aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(9, webDirectory.Subdirectories.Count);
            Assert.Equal("$RECYCLE.BIN", webDirectory.Subdirectories[0].Name);
            Assert.Equal("http://localhost/?dir=%24RECYCLE.BIN", webDirectory.Subdirectories[0].Url);
            Assert.Single(webDirectory.Files);
            Assert.Equal("Accel World.zip", webDirectory.Files[0].FileName);
            Assert.Equal(396841, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Check for https://www.directorylister.com/
        /// Url: http://sr1.animelist1.ir/index.php/?dir=Oda%20Nobuna%20No%20Yabou
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing38bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(12, webDirectory.Files.Count);
            Assert.Equal("Oda Nobuna No Yabou.01.Uncens.BD.[720p].AnimeList.ir.mkv", webDirectory.Files[0].FileName);
            Assert.Equal(118101115, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://ftp.mozilla.org/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing39aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("pub", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("favicon.ico", webDirectory.Files[0].FileName);
            Assert.Equal(304, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://ftp.mozilla.org/pub/artwork/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing39bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("eps_files_3.zip", webDirectory.Files[0].FileName);
            Assert.Equal(3145728, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.funreading.com.hk/primary/computer/xsl/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing40aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(6, webDirectory.Subdirectories.Count);
            Assert.Equal("assessment", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("merp_interface_shell.xsl", webDirectory.Files[0].FileName);
            Assert.Equal(9011, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.funreading.com.hk/primary/computer/download/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing40bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://www.igpp.ucla.edu/public/THEMIS/SCI/Pubs/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing41aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(19, webDirectory.Subdirectories.Count);
            Assert.Equal("2004_Refereed", webDirectory.Subdirectories[0].Name);
            Assert.Equal(41, webDirectory.Files.Count);
            Assert.Equal("APL_logo.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(10547, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.igpp.ucla.edu/public/THEMIS/SCI/Pubs/Nuggets/2009_nuggets/runov/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing41bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(9, webDirectory.Files.Count);
            Assert.Equal("aurora.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(38912, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://perso.crans.org/besson/publis/Nginx-Fancyindex-Theme/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing42aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("Nginx-Fancyindex-Theme-dark", webDirectory.Subdirectories[0].Name);
            Assert.Equal(11, webDirectory.Files.Count);
            Assert.Equal("HEADER.md", webDirectory.Files[0].FileName);
            Assert.Equal(271, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://perso.crans.org/besson/publis/Nginx-Fancyindex-Theme/screenshots/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing42bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(6, webDirectory.Files.Count);
            Assert.Equal("Nginx-Fancyindex-Theme__example1.png", webDirectory.Files[0].FileName);
            Assert.Equal(216064, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://www.sec.gov/Archives/edgar/full-index/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing43aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(26, webDirectory.Subdirectories.Count);
            Assert.Equal("1993", webDirectory.Subdirectories[0].Name);
            Assert.Equal(18, webDirectory.Files.Count);
            Assert.Equal("company.gz", webDirectory.Files[0].FileName);
            Assert.Equal(2908160, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://www.sec.gov/Archives/edgar/full-index/2018/QTR1/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing43bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(28, webDirectory.Files.Count);
            Assert.Equal("company.gz", webDirectory.Files[0].FileName);
            Assert.Equal(4362240, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.ci.camas.wa.us/records_portal/Public%20Record%20Requests/2017/2352568%20Richardson%20PD%20Preapps/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing44aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(12, webDirectory.Subdirectories.Count);
            Assert.Equal("031017 Installment", webDirectory.Subdirectories[0].Name);
            Assert.Equal(5, webDirectory.Files.Count);
            Assert.Equal("PA14-05  Patty's place notes.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(155648, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.ci.camas.wa.us/records_portal/Public%20Record%20Requests/2017/2352568%20Richardson%20PD%20Preapps/PA13-05%20Vactor%20Project/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing44bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("ANALYSIS MAPS.pub", webDirectory.Files[0].FileName);
            Assert.Equal(357376, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://tucrrc.utulsa.edu/members/index.php?dir=Headon+-+NAPARS+Joint+Conference+2014%2FMapping+Data%2FLightpoint+Photogrammetry+Project%2FVolvo+960%2FPre-impact%2F
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing45aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("backups", webDirectory.Subdirectories[0].Name);
            Assert.Equal(25, webDirectory.Files.Count);
            Assert.Equal("1997 Volvo 960 - Pre.dxf", webDirectory.Files[0].FileName);
            Assert.Equal(19456, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://tucrrc.utulsa.edu/members/index.php?dir=Headon+-+NAPARS+Joint+Conference+2014%2FMapping+Data%2FLightpoint+Photogrammetry+Project%2FVolvo+960%2FPre-impact%2Fbackups%2F
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing45bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("1997 Volvo 960 - Pre_BACKUP1.iwp", webDirectory.Files[0].FileName);
            Assert.Equal(305152, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://mirror.internode.on.net/pub/eclipse/collections/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing46aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(15, webDirectory.Subdirectories.Count);
            Assert.Equal("10.0.0.M2", webDirectory.Subdirectories[0].Name);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("gsc-ec-converter-7.1.0.tar", webDirectory.Files[0].FileName);
            Assert.Equal(9871360, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://mirror.internode.on.net/pub/eclipse/collections/pre-release/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing46bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("gsc-ec-converter-7.1.0-SNAPSHOT.tar", webDirectory.Files[0].FileName);
            Assert.Equal(9871360, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://mirror.internode.on.net/pub/eclipse/collections/release/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing46cAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://mirror.internode.on.net/pub/eclipse/collections/pre-release/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing47aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("DOME", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("naklejka22.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(200704, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://mirror.internode.on.net/pub/eclipse/collections/release/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing47bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(37, webDirectory.Files.Count);
            Assert.Equal("2014-DO-1.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(1444864, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.rayburn-web.co.uk/DirectoryList.asp?SD=United%20Kingdom%20and%20Ireland/AGA/2.%20AGA%20Discontinued/Aga%20electric%20cookers/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing48aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("AGA 6-4 Ceramic Hob Pre Oven Efficiency", webDirectory.Subdirectories[0].Name);
            Assert.Equal(8, webDirectory.Files.Count);
            Assert.Equal("Aga Elect 13amp 2 and 4 oven manual 03-07 EINS 513935 (UK).pdf", webDirectory.Files[0].FileName);
            Assert.Equal(0, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.rayburn-web.co.uk/DirectoryList.asp?SD=United%20Kingdom%20and%20Ireland/AGA/2.%20AGA%20Discontinued/Aga%20electric%20cookers/AGA%206-4%20Ceramic%20Hob%20Pre%20Oven%20Efficiency/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing48bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("Aga Six Four electric manual11-09 EINS 513129.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(0, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://bailey.persona-pi.com/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing49aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(4, webDirectory.Subdirectories.Count);
            Assert.Equal("Main Web", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://bailey.persona-pi.com/?dir=Main%20Web
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing49bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(22, webDirectory.Files.Count);
            Assert.Equal("Bexhill Combe Valley - resized.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(18534, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://bailey.persona-pi.com/?dir=Public-Inquiries
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing49cAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(22, webDirectory.Subdirectories.Count);
            Assert.Equal("A4194-Herefordshire", webDirectory.Subdirectories[0].Name);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("25.10.18-Programme.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(262124, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://twistedmatrix.com/Releases/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing50aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(22, webDirectory.Subdirectories.Count);
            Assert.Equal("Conch", webDirectory.Subdirectories[0].Name);
            Assert.Equal(92, webDirectory.Files.Count);
            Assert.Equal("Twisted-1.3.0.tar.bz2", webDirectory.Files[0].FileName);
            Assert.Equal(5242880, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://twistedmatrix.com/Releases/reality/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing50bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("old", webDirectory.Subdirectories[0].Name);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("Inheritance-0.15.5.tar.gz", webDirectory.Files[0].FileName);
            Assert.Equal(27648, webDirectory.Files[0].FileSize);
        }
    }
}