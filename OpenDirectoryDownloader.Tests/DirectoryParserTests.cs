using OpenDirectoryDownloader.Shared.Models;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class DirectoryParserTests
    {
        private static readonly Regex TestMethodRegex = new Regex(@"<Test(\w+)Async>");

        private static string GetSample()
        {
            // Ugly, but it works
            string fileName = TestMethodRegex.Match(new StackTrace().GetFrame(1).GetMethod().DeclaringType.Name).Groups[1].Value;

            return File.ReadAllText($"Samples\\{fileName}.html.dat");
        }

        private static async Task<WebDirectory> ParseHtml(string html, string url = "http://localhost/")
        {
            return await DirectoryParser.ParseHtml(new WebDirectory(null) { Url = url }, html);
        }

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
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
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
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
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
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
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
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
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

        /// <summary>
        /// Url: https://twistedmatrix.com/Releases/reality/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing51aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("pre-bid_conference", webDirectory.Subdirectories[0].Name);
            Assert.Equal(16, webDirectory.Files.Count);
            Assert.Equal("00_RFP_800-10-801.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(518042, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://twistedmatrix.com/Releases/reality/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing51bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("800-10-801_Pre-Bid.wrf", webDirectory.Files[0].FileName);
            Assert.Equal(6396314, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://media.sas.upenn.edu/pennsound/authors/Abel/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing52aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(25, webDirectory.Subdirectories.Count);
            Assert.Equal("Bridge Bookshop (12-10-87)", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("Abel-David.jpg", webDirectory.Files[0].FileName);
            Assert.Equal(315392, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://media.sas.upenn.edu/pennsound/authors/Abel/Bridge%20Bookshop%20(12-10-87)/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing52bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("OBrien-Michael_Bridge-Bookshop_12-10-87.wav", webDirectory.Files[0].FileName);
            Assert.Equal(179306496, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.pongkombat.com/music/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing53aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(27, webDirectory.Files.Count);
            Assert.Equal("dun-dun-dun.m4a", webDirectory.Files[0].FileName);
            Assert.Equal(99154, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.pongkombat.com/sounds/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing53bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(264, webDirectory.Files.Count);
            Assert.Equal("acid.m4a", webDirectory.Files[0].FileName);
            Assert.Equal(356782, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://myrkr.info/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing54aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(21, webDirectory.Subdirectories.Count);
            Assert.Equal("Hyphenator_chrome", webDirectory.Subdirectories[0].Name);
            Assert.Equal(23, webDirectory.Files.Count);
            Assert.Equal("A_Really_long_name_that_should_never_be_used.epub", webDirectory.Files[0].FileName);
            Assert.Equal(0, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://myrkr.info/ada/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing54bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("piece.adb", webDirectory.Files[0].FileName);
            Assert.Equal(111, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ftpmirror.your.org/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing55aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal(".well-known", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("README.txt", webDirectory.Files[0].FileName);
            Assert.Equal(922, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ftpmirror.your.org/pub/openstreetmap/planet/2019/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing55bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(6, webDirectory.Files.Count);
            Assert.Equal("changesets-190107.osm.bz2", webDirectory.Files[0].FileName);
            Assert.Equal(2684354560, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ftp.pigwa.net/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing56aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("stuff", webDirectory.Subdirectories[0].Name);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("Changelog.html", webDirectory.Files[0].FileName);
            Assert.Equal(173056, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://ftp.pigwa.net/stuff/misc/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing56bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(33, webDirectory.Subdirectories.Count);
            Assert.Equal("Aladin", webDirectory.Subdirectories[0].Name);
            Assert.Equal(15, webDirectory.Files.Count);
            Assert.Equal("Atarian_ttf_font.zip", webDirectory.Files[0].FileName);
            Assert.Equal(104448, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://index-of.es/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing57aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(69, webDirectory.Subdirectories.Count);
            Assert.Equal("Android", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://index-of.es/Varios-2/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing57bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(507, webDirectory.Files.Count);
            Assert.Equal("100 Deadly Skills.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(58720256, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://www.omnimaga.org/files/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing58aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("Old-Calculator-RPGs-Headquarter-Archive", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("master.index", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://www.omnimaga.org/files/Old-Calculator-RPGs-Headquarter-Archive/Casio-AFX-series-Graph-100-RPGs/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing58bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(6, webDirectory.Files.Count);
            Assert.Equal("aronstyle.zip", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://ftp.yzu.edu.tw/Linux/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing59aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(66, webDirectory.Subdirectories.Count);
            Assert.Equal("alpine", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://ftp.yzu.edu.tw/Linux/packman/suse/11.1/repodata/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing59bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(6, webDirectory.Files.Count);
            Assert.Equal("filelists.xml.gz", webDirectory.Files[0].FileName);
            Assert.Equal(531456, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://grossgang.com/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing60aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(19, webDirectory.Subdirectories.Count);
            Assert.Equal("Gabriel's Podcast intros", webDirectory.Subdirectories[0].Name);
            Assert.Equal(20, webDirectory.Files.Count);
            Assert.Equal("BGZ Audio Archive.7z", webDirectory.Files[0].FileName);
            Assert.Equal(8160437862, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://grossgang.com/Gabriel's%20Podcast%20intros/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing60bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(9, webDirectory.Files.Count);
            Assert.Equal("Intro.mp3", webDirectory.Files[0].FileName);
            Assert.Equal(342016, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://stepmaniathings.com/files/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing61aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(328, webDirectory.Files.Count);
            Assert.Equal("(3rd) Charmy (A).smzip", webDirectory.Files[0].FileName);
            Assert.Equal(61440, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://unrealtournament.99.free.fr/utfiles/index.php?dir=/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing62aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(38, webDirectory.Subdirectories.Count);
            Assert.Equal("Admin", webDirectory.Subdirectories[0].Name);
            Assert.Equal(5, webDirectory.Files.Count);
            Assert.Equal("beta_anims.zip", webDirectory.Files[0].FileName);
            Assert.Equal(5033165, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://unrealtournament.99.free.fr/utfiles/index.php?dir=Admin/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing62bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(22, webDirectory.Subdirectories.Count);
            Assert.Equal("ActorCLP", webDirectory.Subdirectories[0].Name);
            Assert.Equal(31, webDirectory.Files.Count);
            Assert.Equal("ActorEditor.rar", webDirectory.Files[0].FileName);
            Assert.Equal(12698, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://lolicore.org
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing63aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(170, webDirectory.Subdirectories.Count);
            Assert.Equal("_INCOMING_", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://lolicore.org/999%20Recordings/[TCNCD001]%20%28C68%29%20HARDCORE%20TECHNIQUE%20%28DJ%20TECHNORCH%29%20-%20Gothic%20System%EF%BC%9A%20Trancecore%20Meets%20Gabber/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing63bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(14, webDirectory.Files.Count);
            Assert.Equal("01. DJ TECHNORCH — 極楽鳥 (Oldskool mix).ogg", webDirectory.Files[0].FileName);
            Assert.Equal(7340032, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://taylorlove.info/dl/ut/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing64aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("Coco 2017 HD-TS X264 HQ-CPG", webDirectory.Subdirectories[0].Name);
            Assert.Equal(15, webDirectory.Files.Count);
            Assert.Equal("Aquaman 2018 720p.mp4", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://taylorlove.info/dl/ut/____SeenIt/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing64bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(12, webDirectory.Files.Count);
            Assert.Equal("Ant.Man.2015.720p.HDRip.x264.AAC-ETRG.mp4", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://sten.planet.ee/mi/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing65aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(2, webDirectory.Subdirectories.Count);
            Assert.Equal("@mi", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://sten.planet.ee/mi/lpw/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing65bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(35, webDirectory.Files.Count);
            Assert.Equal("S.L, MI (01.03.12) HardRockLPW, Soundgarden-\"Superunknown\".wav", webDirectory.Files[0].FileName);
            Assert.Equal(55574528, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.meggamusic.co.uk/winamp/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing66aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(12, webDirectory.Subdirectories.Count);
            Assert.Equal("558_2908", webDirectory.Subdirectories[0].Name);
            Assert.Equal(81, webDirectory.Files.Count);
            Assert.Equal("Casso_Blax_-_Falling_Star.m4a.zip", webDirectory.Files[0].FileName);
            Assert.Equal(1363149, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://www.meggamusic.co.uk/winamp/dca/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing66bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(2, webDirectory.Files.Count);
            Assert.Equal("index.php", webDirectory.Files[0].FileName);
            Assert.Equal("winamp_dts.exe", webDirectory.Files[1].FileName);
            Assert.Equal(5120, webDirectory.Files[0].FileSize);
            Assert.Equal(181453, webDirectory.Files[1].FileSize);
        }

        /// <summary>
        /// Url: http://wanderinstylefitness.com/books/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing67aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(11, webDirectory.Subdirectories.Count);
            Assert.Equal("AP Biology", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("the-bell-curve.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(41313894, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://wanderinstylefitness.com/books/AP%20Biology/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing67bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Single(webDirectory.Files);
            Assert.Equal("5 Steps to a 5 AP Biology.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(23068672, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://dl.mojoo.ir/upload/film/tv-shows/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing68aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(110, webDirectory.Subdirectories.Count);
            Assert.Equal("12Monkeys", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("directory.zip", webDirectory.Files[0].FileName);
            Assert.Equal(45056, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://dl.mojoo.ir/upload/film/tv-shows/?dir=12Monkeys/s1
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing68bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), "http://dl.mojoo.ir/upload/film/tv-shows/?dir=12Monkeys");

            Assert.Equal("tv-shows", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(14, webDirectory.Files.Count);
            Assert.Equal("12.Monkeys.S01E01.480p.mkv", webDirectory.Files[0].FileName);
            Assert.Equal(195305472, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://cloud.nekoeye.me/?/Storage/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing69aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(6, webDirectory.Subdirectories.Count);
            Assert.Equal("Animation", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://cloud.nekoeye.me/?/Storage/Animation/Fate%20Zero/%5BVCB-Studio%5D%20Fate%20Zero%20%5BMa10p_1080p%5D/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing69bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("CDs", webDirectory.Subdirectories[0].Name);
            Assert.Equal(28, webDirectory.Files.Count);
            Assert.Equal("[VCB-Studio] Fate Zero [01][Ma10p_1080p][x265_flac].mkv", webDirectory.Files[0].FileName);
            Assert.Equal(2254857830, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://v1.wml8.com/A:/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing70aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(29, webDirectory.Subdirectories.Count);
            Assert.Equal("upload10", webDirectory.Subdirectories[0].Name);
            Assert.Single(webDirectory.Files);
            Assert.Equal("index.html", webDirectory.Files[0].FileName);
            Assert.Equal(24, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://v1.wml8.com/A:/upload10
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing70bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(50, webDirectory.Files.Count);
            Assert.Equal("1-lafbd-63-misaki-tsubasa-laforet-girl-63_sh.mp4", webDirectory.Files[0].FileName);
            Assert.Equal(1610612736, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://ftp.sunet.se/mirror/ubuntu-releases/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing71aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(17, webDirectory.Subdirectories.Count);
            Assert.Equal("12.04.5", webDirectory.Subdirectories[0].Name);
            Assert.Equal("Ubuntu 12.04.5 LTS (Precise Pangolin)", webDirectory.Subdirectories[0].Description);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://ftp.sunet.se/mirror/ubuntu-releases/14.04.5/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing71bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(35, webDirectory.Files.Count);
            Assert.Equal("MD5SUMS", webDirectory.Files[0].FileName);
            Assert.Equal(307, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://www.mmnt.net/db/0/0/89.179.242.201/Music
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing72aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(9, webDirectory.Subdirectories.Count);
            Assert.Equal("5.1", webDirectory.Subdirectories[0].Name);
            Assert.Equal(9, webDirectory.Files.Count);
            Assert.Equal("1.fpl", webDirectory.Files[0].FileName);
            Assert.Equal(1132462, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://www.mmnt.net/db/0/0/89.179.242.201/Music/Disk
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing72bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("Metallica - Black Album", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("Alan_Parsons_-_Turn_Of_A_Friendly_Card.iso", webDirectory.Files[0].FileName);
            Assert.Equal(3661459620, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://repo.zenk-security.com/?dir=.
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing73aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(21, webDirectory.Subdirectories.Count);
            Assert.Equal("Conferences", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: https://repo.zenk-security.com/?dir=./Magazine%20E-book
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing73bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), url: "https://repo.zenk-security.com/?dir=./Magazine%20E-book");

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(4, webDirectory.Subdirectories.Count);
            Assert.Equal("ActuSecu", webDirectory.Subdirectories[0].Name);
            Assert.Equal(80, webDirectory.Files.Count);
            Assert.Equal("Android Hacker's Handbook.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(9437184, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://4ac704e521f7.sn.mynetname.net/server/events/xxx/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing74aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(114, webDirectory.Subdirectories.Count);
            Assert.Equal("(000 papa intenso)", webDirectory.Subdirectories[0].Name);
            Assert.Equal(33, webDirectory.Files.Count);
            Assert.Equal("ALS Scan - Franziska Facella & Sara Jaymes - Clutch Hitter BTS (12-10-18).mp4", webDirectory.Files[0].FileName);
            // TODO: There is a filesize in it, not yet fixed
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://4ac704e521f7.sn.mynetname.net/server/events/Adobe%20Acrobat%20Pro%20IX/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing74bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Single(webDirectory.Subdirectories);
            Assert.Equal("Crack", webDirectory.Subdirectories[0].Name);
            Assert.Equal(4, webDirectory.Files.Count);
            Assert.Equal("AcrobatPro.exe", webDirectory.Files[0].FileName);
            // TODO: There is a filesize in it, not yet fixed
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

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
            WebDirectory webDirectory = await ParseHtml(GetSample());

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
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(487, webDirectory.Files.Count);
            Assert.Equal("Q836 (Side A).mp3", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
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
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }
    }
}