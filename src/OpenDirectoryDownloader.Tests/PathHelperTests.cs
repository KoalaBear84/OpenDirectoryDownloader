using OpenDirectoryDownloader.Helpers;
using System.Runtime.InteropServices;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

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

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			Assert.Equal("http___localhost_", validUrl);
		}
		else
		{
			Assert.Equal("http:__localhost_", validUrl);
		}
	}

	/// <summary>
	/// Test 2
	/// </summary>
	/// <returns>Nothing</returns>
	[Fact]
	public void Test02()
	{
		string validUrl = PathHelper.GetValidPath("https://stackoverflow.com/questions/146134/how-to-remove-illegal-characters-from-path-and-filenames");

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			Assert.Equal("https___stackoverflow.com_questions_146134_how-to-remove-illegal-characters-from-path-and-filenames", validUrl);
		}
		else
		{
			Assert.Equal("https:__stackoverflow.com_questions_146134_how-to-remove-illegal-characters-from-path-and-filenames", validUrl);
		}
	}
}
