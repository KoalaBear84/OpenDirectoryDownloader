using OpenDirectoryDownloader.Helpers;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class PathHelperTests
    {
        /// <summary>
        /// Test 1
        /// </summary>
        /// <returns>Nothing</returns>
        [Fact]
        public void Test01()
        {
            string validUrl = PathHelper.GetValidPath("http://localhost/");
            Assert.Equal("http___localhost_", validUrl);
        }

        /// <summary>
        /// Test 2
        /// </summary>
        /// <returns>Nothing</returns>
        [Fact]
        public void Test02()
        {
            string validUrl = PathHelper.GetValidPath("https://stackoverflow.com/questions/146134/how-to-remove-illegal-characters-from-path-and-filenames");
            Assert.Equal("https___stackoverflow.com_questions_146134_how-to-remove-illegal-characters-from-path-and-filenames", validUrl);
        }
    }
}
