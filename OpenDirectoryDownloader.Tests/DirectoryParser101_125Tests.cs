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

    } 
}