using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Threading.Tasks;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class DirectoryParser101_125Tests : DirectoryParserTests
    {
        /// <summary>
        /// Url: https://thetrove.net/Books/Dungeons%20&%20Dragons/5th%20Edition%20(5e)/3rd%20Party/The%20Deck%20of%20Many/Deck%20of%20Many%20-%20Animated%20Spells%20Master%20V.02%20[Mar%202019]/
        /// Added for directories starting with a space, who invented that??
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing101aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample(), "https://thetrove.net/Books/Dungeons%20&%20Dragons/5th%20Edition%20(5e)/3rd%20Party/The%20Deck%20of%20Many/Deck%20of%20Many%20-%20Animated%20Spells%20Master%20V.02%20[Mar%202019]/");

            Assert.Equal("Deck of Many - Animated Spells Master V.02 [Mar 2019]", webDirectory.Name);
            Assert.Equal(2, webDirectory.Subdirectories.Count);
            Assert.Equal(" Cantrips Gifs", webDirectory.Subdirectories[0].Name);
            Assert.Equal(7, webDirectory.Files.Count);
            Assert.Equal("Rulebooklet-PNP.pdf", webDirectory.Files[0].FileName);
            Assert.Equal(14680064, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: https://media.thqnordic.com/?dir=Monkey_King/Trailer
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing102aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(3, webDirectory.Subdirectories.Count);
            Assert.Equal("PC", webDirectory.Subdirectories[0].Name);
            Assert.Equal(3, webDirectory.Files.Count);
            Assert.Equal("MonkeyKing_DLC2_Trailer_PC_ESRB_EN.mp4", webDirectory.Files[0].FileName);
            Assert.Equal(70254592, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://190.213.27.232/?path=./Comics/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing103aAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(5, webDirectory.Subdirectories.Count);
            Assert.Equal("@eaDir", webDirectory.Subdirectories[0].Name);
            Assert.Equal(57, webDirectory.Files.Count);
            Assert.Equal("2020 Force Works 002 (2020) (Digital) (Zone-Empire).cbr", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }

        /// <summary>
        /// Url: http://190.213.27.232/?path=./Comics/MARVEL/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing103bAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Equal(15, webDirectory.Subdirectories.Count);
            Assert.Equal("Avengers", webDirectory.Subdirectories[0].Name);
            Assert.Empty(webDirectory.Files);
        }

        /// <summary>
        /// Url: http://190.213.27.232/?path=./Comics/Crossovers/Amalgam%20Comics/
        /// </summary>
        [Fact]
        public async Task TestDirectoryListing103cAsync()
        {
            WebDirectory webDirectory = await ParseHtml(GetSample());

            Assert.Equal("ROOT", webDirectory.Name);
            Assert.Empty(webDirectory.Subdirectories);
            Assert.Equal(24, webDirectory.Files.Count);
            Assert.Equal("Amazon.cbr", webDirectory.Files[0].FileName);
            Assert.Equal(-1, webDirectory.Files[0].FileSize);
        }
    } 
}