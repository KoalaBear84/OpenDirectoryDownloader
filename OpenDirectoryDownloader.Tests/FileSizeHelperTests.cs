using OpenDirectoryDownloader.Helpers;
using Xunit;

namespace OpenDirectoryDownloader.Tests
{
    public class FileSizeHelperTests
    {
        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;
        const long TB = 1024 * GB;
        const long PB = 1024 * TB;

        /// <summary>
        /// Test 1
        /// </summary>
        [Fact]
        public void Test01()
        {
            Assert.Equal(1234567, FileSizeHelper.ParseFileSize("1234567 bytes"));
            Assert.Equal(KB, FileSizeHelper.ParseFileSize("1 kB"));
            Assert.Equal(KB, FileSizeHelper.ParseFileSize("1 K"));
            Assert.Equal(MB, FileSizeHelper.ParseFileSize("1 MB"));
            Assert.Equal(MB, FileSizeHelper.ParseFileSize("1 M"));
            Assert.Equal(GB, FileSizeHelper.ParseFileSize("1 GB"));
            Assert.Equal(GB, FileSizeHelper.ParseFileSize("1 G"));
            Assert.Equal(TB, FileSizeHelper.ParseFileSize("1 TB"));
            Assert.Equal(TB, FileSizeHelper.ParseFileSize("1 T"));
            Assert.Equal(PB, FileSizeHelper.ParseFileSize("1 PB"));
            Assert.Equal(PB, FileSizeHelper.ParseFileSize("1 P"));
        }

        /// <summary>
        /// Test 2
        /// </summary>
        [Fact]
        public void Test02()
        {
            Assert.Equal(5 * MB, FileSizeHelper.ParseFileSize("⇩5MB"));
            Assert.Equal(1.5 * KB, FileSizeHelper.ParseFileSize("1.5 kB"));
            Assert.Equal(15 * KB, FileSizeHelper.ParseFileSize("1,5 kB"));
            Assert.Equal(-1, FileSizeHelper.ParseFileSize("-1"));
        }
    }
}
