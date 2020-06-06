using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class DirectoryParser000_25Tests : DirectoryParserTests
    {
        /// <summary>
        /// Url: http://178.216.250.167/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing01aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("15", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("_yetishare_test_1521945706.txt", webDirectory.Files[0].FileName);
            Assert.Equal(20, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://taz.newffr.com/TAZ/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing02aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(16, webDirectory.Subdirectories.Count);
            Assert.Equal("[en] Ascii, Sex and Fun for Geek", webDirectory.Subdirectories[0].Description);
            Assert.Equal("Ascii_PR0n", webDirectory.Subdirectories[0].Name);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("Attack_tor.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(293888, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://taz.newffr.com/TAZ/Cryptologie/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing02bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(13, webDirectory.Subdirectories.Count);
            Assert.Equal("Full Contents of Bruce Schneier's Applied Cryptography Disks", webDirectory.Subdirectories[4].Description);
            Assert.Equal("applied-crypto", webDirectory.Subdirectories[4].Name);
            Assert.Equal(19, webDirectory.Files.Count);
            Assert.Equal("Chosen-Ciphertext Attacks on Optimized NTRU.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(153600, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://firstcontact.world/js/ckeditor/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing03aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("adapters", webDirectory.Subdirectories[0].Name);
            Assert.Equal(7, webDirectory.Files.Count);
            Assert.Equal("CHANGES.md", webDirectory.Files[0].FileName);
            Assert.Equal(149504, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://firstcontact.world/js/ckeditor/lang/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing03bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(68, webDirectory.Files.Count);
            Assert.Equal("af.js", webDirectory.Files[0].FileName);
            Assert.Equal(17408, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://repo.palkeo.com/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing04aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(7, webDirectory.Subdirectories.Count);
            Assert.Equal("algo", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("lefrido.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(9437184, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://repo.palkeo.com/esprit/soci%C3%A9t%C3%A9/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing04bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(4, webDirectory.Subdirectories.Count);
            Assert.Equal("problèmes", webDirectory.Subdirectories[0].Name);
            Assert.Equal(16, webDirectory.Files.Count);
            Assert.Equal("Défense maison des jeunes au Danemark.avi", webDirectory.Files[0].FileName);
            Assert.Equal(510656512, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://kip.macemicro.com/Kip's%20Files/Other/VBA/Pokemon%20games/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing05aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(6, webDirectory.Subdirectories.Count);
            Assert.Equal("Crystal Maps", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("Pokemon Crystal.sg1.SGM", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://kip.macemicro.com/Kip's%20Files/Other/VBA/Pokemon%20games/Pokemon%20Roms/DS%20Roms/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing05bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(4, webDirectory.Subdirectories.Count);
            Assert.Equal("Nintendo DS", webDirectory.Subdirectories[0].Name);
            Assert.Equal(7, webDirectory.Files.Count);
            Assert.Equal("Pokemon Dash.zip", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://3dmusic.com/content/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing06aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(17, webDirectory.Subdirectories.Count);
            Assert.Equal("classic_skin_dark", webDirectory.Subdirectories[0].Name);
            Assert.Equal(15, webDirectory.Files.Count);
            Assert.Equal("downloader.php", webDirectory.Files[0].FileName);
            Assert.Equal(4096, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://3dmusic.com/content/videos/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing06bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("360.mp4", webDirectory.Files[0].FileName);
            Assert.Equal(22360064, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://kunalsdatabase.com/files/popularmusic/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing07aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(144, webDirectory.Subdirectories.Count);
            Assert.Equal("009 Sound System", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("AlbumArtSmall.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(7782, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://kunalsdatabase.com/files/popularmusic/009%20Sound%20System/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing07bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(17, webDirectory.Subdirectories.Count);
            Assert.Equal("009 Sound System (Album)", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://s1.tinydl.info/Movies2/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing08aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(30, webDirectory.Subdirectories.Count);
            Assert.Equal("#FollowFriday 2016", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://s1.tinydl.info/Movies2/&%238216;G&%238217;%20Men%201935/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing08bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Single(webDirectory.Files);
            Assert.Equal("G_Men_1935_720p_WEB-DL_Unknown_TINYMZ.mkv", webDirectory.Files[0].FileName);
            Assert.Equal(907542528, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://music.opiums.ru/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing09aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("Part_00", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("!opiums.ru.htm", webDirectory.Files[0].FileName);
            Assert.Equal(61, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://music.opiums.ru/Part_00/01_Beam%20Breakers/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing09bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(9, webDirectory.Files.Count);
            Assert.Equal("01-Main Theme.mp3", webDirectory.Files[0].FileName);
            Assert.Equal(7580646, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://wilmingtonnetworks.ddns.net/music/Music/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing10aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(183, webDirectory.Subdirectories.Count);
            Assert.Equal("(WWW.HiLToWN-MuSiC.DL.AM)", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://wilmingtonnetworks.ddns.net/music/Music/Bon%20Jovi/Cross%20Road/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing10bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Single(webDirectory.Files);
            Assert.Equal("12 I'll Be There for You.m4a", webDirectory.Files[0].FileName);
            Assert.Equal(11239374, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://webdav.gnunix.co.kr/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing11aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(6, webDirectory.Subdirectories.Count);
            Assert.Equal("aspnet_client", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("web.config", webDirectory.Files[0].FileName);
            Assert.Equal(168, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://webdav.gnunix.co.kr/Lecture/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing11bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(19, webDirectory.Subdirectories.Count);
            Assert.Equal("[통기타]어쿠스틱 기타 강좌", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://mediaset.sdasofia.org/MEDIA%20SET/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing12aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(63, webDirectory.Subdirectories.Count);
            Assert.Equal("A. MEDIA_NEWS", webDirectory.Subdirectories[0].Name);
            Assert.Equal(8, webDirectory.Files.Count);
            Assert.Equal("Cosmic_Conflict_-_The_Origin_of_Evil_HomeCinema.avi", webDirectory.Files[0].FileName);
            Assert.Equal(423046686, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://mediaset.sdasofia.org/MEDIA%20SET/index.php?path=A.+MEDIA_NEWS%2FPUBLICATIONS/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing12bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("0.E.Velinov- avtora_files", webDirectory.Subdirectories[0].Name);
            Assert.Equal(15, webDirectory.Files.Count);
            Assert.Equal("0.E.Velinov- avtora.doc", webDirectory.Files[0].FileName);
            Assert.Equal(50688, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Check for godir directory listing
        /// Url: https://thetrove.net/Tools/index.html
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing13aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), "https://thetrove.net/Tools/index.html");

            Assert.Equal(string.Empty, webDirectory.Name);
            Assert.Equal(7, webDirectory.Subdirectories.Count);
            Assert.Equal("AD&D Core Rules CD-Rom", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("El Raja Key Archive.7z", webDirectory.Files[0].FileName);
            Assert.Equal(4294967296, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Check for godir directory listing
        /// Url: https://thetrove.net/Assets/D&D%20Homebrew/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing13bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), "https://thetrove.net/Assets/D&D Homebrew/index.html");

            Assert.Equal(string.Empty, webDirectory.Name);
            Assert.Equal(9, webDirectory.Subdirectories.Count);
            Assert.Equal("3rd Edition", webDirectory.Subdirectories[0].Name);
            Assert.Equal(5, webDirectory.Files.Count);
            Assert.Equal("Designers & Dragons 00s.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(14680064, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Check for godir directory listing
        /// Url: https://thetrove.net/Assets/Map%20Assets/2010-Fantasy/Fantasy/BearSkin%20Rug%20+%20Probonos%20hanging%20antelope/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing13cAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), "https://thetrove.net/Assets/Map Assets/2010-Fantasy/Fantasy/BearSkin Rug + Probonos hanging antelope/index.html");

            Assert.Equal(string.Empty, webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(5, webDirectory.Files.Count);
            Assert.Equal("Antelope_1_PB.png", webDirectory.Files[0].FileName);
            Assert.Equal(24576, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://foxious.persiangig.com/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing14aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(20, webDirectory.Subdirectories.Count);
            Assert.Equal("AP-medals", webDirectory.Subdirectories[0].Name);
            Assert.Equal(49, webDirectory.Files.Count);
            Assert.Equal("Brushes.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(136192, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://foxious.persiangig.com/APpro/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing14bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("dark1", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("greensleves.mp3", webDirectory.Files[0].FileName);
            Assert.Equal(970752, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://slav0nic.org.ua/static/books/python/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing15aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("curses_module", webDirectory.Subdirectories[0].Name);
            Assert.Equal(60, webDirectory.Files.Count);
            Assert.Equal("[OReilly].Python.Programming.on.Win32.chm", webDirectory.Files[0].FileName);
            Assert.Equal(5138022, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://slav0nic.org.ua/static/books/python/curses_module/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing15bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(8, webDirectory.Files.Count);
            Assert.Equal("curses.html", webDirectory.Files[0].FileName);
            Assert.Equal(13312, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://finalmovie.parsaspace.com/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing16aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(7, webDirectory.Subdirectories.Count);
            Assert.Equal("Animation", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://finalmovie.parsaspace.com/Series/A%20Discovery%20of%20Witches/Season%201/1080p/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing16bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(6, webDirectory.Files.Count);
            Assert.Equal("A.Discovery.Of.Witches.S01E01.1080p.x265.MeGusta_(_FinalMovie_).mkv", webDirectory.Files[0].FileName);
            Assert.Equal(461670917, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://kittybitch.0x44.me/art/roms/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing17aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("new", webDirectory.Subdirectories[0].Name);
            Assert.Equal(53, webDirectory.Files.Count);
            Assert.Equal("0028 - Kirby - Canvas Curse (U)_trim.nds", webDirectory.Files[0].FileName);
            Assert.Equal(52953088, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://kittybitch.0x44.me/art/roms/new/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing17bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(12, webDirectory.Files.Count);
            Assert.Equal("0121 - Castlevania - Dawn of Sorrow (U).nds", webDirectory.Files[0].FileName);
            Assert.Equal(49702502, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://tisvcloud.freeway.gov.tw/history/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing18aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(7, webDirectory.Subdirectories.Count);
            Assert.Equal("vd", webDirectory.Subdirectories[0].Name);
            Assert.Equal(19, webDirectory.Files.Count);
            Assert.Equal("vd_value5.xml.gz", webDirectory.Files[0].FileName);
            Assert.Equal(90112, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://tisvcloud.freeway.gov.tw/history/icons/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing18bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(68, webDirectory.Files.Count);
            Assert.Equal("zip.png", webDirectory.Files[0].FileName);
            Assert.Equal(617, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://121.192.178.231:8081/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing19aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(6, webDirectory.Subdirectories.Count);
            Assert.Equal("00.Guest_data", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://121.192.178.231:8081/04.Splicing/CC-CR-all/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing19bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("genomes.txt", webDirectory.Files[0].FileName);
            Assert.Equal(32, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://slot-tech.com/interesting_stuff/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing20aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(99, webDirectory.Subdirectories.Count);
            Assert.Equal("#1 stupidest search phrase I received from Google - ojibwa casino marquette how to know when the slots are going to hit big", webDirectory.Subdirectories[0].Name);
            Assert.Equal(5, webDirectory.Files.Count);
            Assert.Equal("35-36.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(558193, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://slot-tech.com/interesting_stuff/magazine/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing20bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("advertising", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("august 2012.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(34002210, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://borax.polux-hosting.com/madchat/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing21aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(10, webDirectory.Subdirectories.Count);
            Assert.Equal("artgfx", webDirectory.Subdirectories[0].Name);
            Assert.Equal("artcore labyrinthe gfx demos", webDirectory.Subdirectories[0].Description);
            Assert.Single(webDirectory.Files);
            Assert.Equal("repositorium.html", webDirectory.Files[0].FileName);
            Assert.Equal(4915, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://borax.polux-hosting.com/madchat/emags/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing21bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(114, webDirectory.Subdirectories.Count);
            Assert.Equal("zone51", webDirectory.Subdirectories[0].Name);
            Assert.Equal("Ed: Psyko/Underwayy - Philo Excellente (n0.2), Niveau : Mouvant", webDirectory.Subdirectories[1].Description);
            Assert.Equal(53, webDirectory.Files.Count);
            Assert.Equal("xhmag.zip", webDirectory.Files[0].FileName);
            Assert.Equal(28672, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://info.stylee32.net/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing22aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(51, webDirectory.Subdirectories.Count);
            Assert.Equal("4chan", webDirectory.Subdirectories[0].Name);
            Assert.Equal(5, webDirectory.Files.Count);
            Assert.Equal("Contribushun Game.png", webDirectory.Files[0].FileName);
            Assert.Equal(545792, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://info.stylee32.net/4chan/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing22bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(8, webDirectory.Subdirectories.Count);
            Assert.Equal("4chans Guide To Anal", webDirectory.Subdirectories[0].Name);
            Assert.Equal(17, webDirectory.Files.Count);
            Assert.Equal("0.999 = 1.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(110592, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://biomediaproject.com/bmp/files/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing23aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(12, webDirectory.Subdirectories.Count);
            Assert.Equal("banners", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal(".ftpquota", webDirectory.Files[0].FileName);
            Assert.Equal(20, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://biomediaproject.com/bmp/files/banners/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing23bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("BMP3.0 Support Banners", webDirectory.Subdirectories[0].Name);
            Assert.Equal(28, webDirectory.Files.Count);
            Assert.Equal("3.5Petabytes.png", webDirectory.Files[0].FileName);
            Assert.Equal(31744, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://d.05.tn/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing24aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(8, webDirectory.Subdirectories.Count);
            Assert.Equal("Codes", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://d.05.tn/Tool/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing24bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("SSR", webDirectory.Subdirectories[0].Name);
            Assert.Equal(21, webDirectory.Files.Count);
            Assert.Equal("【Bh-Vip】大牛助手VIP破解版.apk", webDirectory.Files[0].FileName);
            Assert.Equal(10407748, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://elsmar.com/pdf_files/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing25aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(59, webDirectory.Subdirectories.Count);
            Assert.Equal("Anaren", webDirectory.Subdirectories[0].Name);
            Assert.Equal(715, webDirectory.Files.Count);
            Assert.Equal("10k11wd3.doc", webDirectory.Files[0].FileName);
            Assert.Equal(249856, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://elsmar.com/pdf_files/Anaren/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing25bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("Anaren_Letter.gif", webDirectory.Files[0].FileName);
            Assert.Equal(47104, webDirectory.Files[0].FileSize);
        }
    }
}