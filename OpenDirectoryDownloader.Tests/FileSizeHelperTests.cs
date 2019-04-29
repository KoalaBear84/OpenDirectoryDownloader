using OpenDirectoryDownloader.Helpers;
using System;
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
            Assert.Equal(KB, FileSizeHelper.ParseFileSize("1 kiB"));
            Assert.Equal(KB, FileSizeHelper.ParseFileSize("1 K"));
            Assert.Equal(KB, FileSizeHelper.ParseFileSize("1 Ko"));

            Assert.Equal(MB, FileSizeHelper.ParseFileSize("1 MB"));
            Assert.Equal(MB, FileSizeHelper.ParseFileSize("1 MiB"));
            Assert.Equal(MB, FileSizeHelper.ParseFileSize("1 M"));
            Assert.Equal(MB, FileSizeHelper.ParseFileSize("1 Mo"));

            Assert.Equal(GB, FileSizeHelper.ParseFileSize("1 GB"));
            Assert.Equal(GB, FileSizeHelper.ParseFileSize("1 GiB"));
            Assert.Equal(GB, FileSizeHelper.ParseFileSize("1 G"));
            Assert.Equal(GB, FileSizeHelper.ParseFileSize("1 Go"));

            Assert.Equal(TB, FileSizeHelper.ParseFileSize("1 TB"));
            Assert.Equal(TB, FileSizeHelper.ParseFileSize("1 TiB"));
            Assert.Equal(TB, FileSizeHelper.ParseFileSize("1 T"));
            Assert.Equal(TB, FileSizeHelper.ParseFileSize("1 To"));

            Assert.Equal(PB, FileSizeHelper.ParseFileSize("1 PB"));
            Assert.Equal(PB, FileSizeHelper.ParseFileSize("1 PiB"));
            Assert.Equal(PB, FileSizeHelper.ParseFileSize("1 P"));
            Assert.Equal(PB, FileSizeHelper.ParseFileSize("1 Po"));
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
            Assert.Equal(3, FileSizeHelper.ParseFileSize("3 OcTeTs"));
            Assert.Equal(8, FileSizeHelper.ParseFileSize("8 OcTeT"));
            Assert.Equal(Math.Round(0.92 * KB), FileSizeHelper.ParseFileSize("0.92 Ko"));
//            Assert.Equal(-1, FileSizeHelper.ParseFileSize("-1"));
        }
    }
}
