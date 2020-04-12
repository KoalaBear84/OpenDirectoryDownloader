using OpenDirectoryDownloader.Shared.Models;
using System.Threading.Tasks;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class DirectoryParser51_75Tests : DirectoryParserTests
    {
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
            Assert.Equal(80, webDirectory.Files.Count);
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
            Assert.Single(webDirectory.Files);
            Assert.Equal("winamp_dts.exe", webDirectory.Files[0].FileName);
            Assert.Equal(181453, webDirectory.Files[0].FileSize);
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
            Assert.Equal(335910500, webDirectory.Files[0].FileSize);

            // This directory listing contains negative file sizes, which will result in "-1"
            Assert.Contains(webDirectory.Files, f => f.FileSize == -1);
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
            Assert.Equal(525508520, webDirectory.Files[0].FileSize);
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
    }
}