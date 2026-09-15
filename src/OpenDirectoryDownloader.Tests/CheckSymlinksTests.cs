using OpenDirectoryDownloader.Shared.Models;
using Serilog;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// Regression coverage for issue #56 phase 3: DirectoryParser.CheckSymlinks/CheckDirectoryTheSame were
/// rewritten to compare a cached WebDirectory.ContentFingerprint instead of re-serializing full
/// Files/Subdirectories lists with JsonConvert on every ancestor comparison. These tests build the same
/// kind of recursive/symlink-loop directory chains the old implementation was meant to catch, and confirm
/// detection still happens at the same ancestor depth (up to 8 levels) with the same behavior.
/// </summary>
public class CheckSymlinksTests
{
	static CheckSymlinksTests()
	{
		// DirectoryParser.CheckSymlinks logs through Program.Logger when it detects a loop; that logger
		// is normally set up by Program.Main, which doesn't run under the test host.
		Program.Logger ??= new LoggerConfiguration().CreateLogger();
	}

	private static WebDirectory CreateFinishedDirectory(WebDirectory parent, string url, string name, IEnumerable<(string FileName, long FileSize)> files, IEnumerable<string> subdirectoryNames = null)
	{
		WebDirectory directory = new(parent)
		{
			Url = url,
			Name = name,
			Finished = true
		};

		foreach ((string fileName, long fileSize) in files)
		{
			directory.Files.Add(new WebFile
			{
				Url = $"{url}{fileName}",
				FileName = fileName,
				FileSize = fileSize
			});
		}

		foreach (string subdirectoryName in subdirectoryNames ?? [])
		{
			directory.Subdirectories.Add(new WebDirectory(directory)
			{
				Url = $"{url}{subdirectoryName}/",
				Name = subdirectoryName
			});
		}

		// Mirrors what OpenDirectoryIndexer.AddProcessedWebDirectory does once a directory's
		// Files/Subdirectories are final: cache the fingerprint for use as an ancestor later.
		directory.ContentFingerprint = directory.ComputeContentFingerprint();

		return directory;
	}

	[Fact]
	public void CheckParsedResults_DetectsImmediateParentSymlinkLoop()
	{
		WebDirectory root = CreateFinishedDirectory(null, "http://localhost/", "ROOT", []);
		WebDirectory a = CreateFinishedDirectory(root, "http://localhost/a/", "a",
			[("file1.bin", 100), ("file2.bin", 200)]);

		// Simulate re-parsing "http://localhost/a/a/" and finding the exact same files as its parent "a" -
		// a classic self-referencing symlink/virtual directory.
		WebDirectory parsedChild = new(a)
		{
			Url = "http://localhost/a/a/",
			Name = "a"
		};
		parsedChild.Files.Add(new WebFile { Url = "http://localhost/a/a/file1.bin", FileName = "file1.bin", FileSize = 100 });
		parsedChild.Files.Add(new WebFile { Url = "http://localhost/a/a/file2.bin", FileName = "file2.bin", FileSize = 200 });

		DirectoryParser.CheckParsedResults(parsedChild, "http://localhost/", checkParents: false);

		Assert.True(parsedChild.Error);
		Assert.Empty(parsedChild.Files);
		Assert.Empty(parsedChild.Subdirectories);
	}

	[Fact]
	public void CheckParsedResults_DetectsLoopSeveralLevelsUp()
	{
		WebDirectory root = CreateFinishedDirectory(null, "http://localhost/", "ROOT", []);
		WebDirectory level1 = CreateFinishedDirectory(root, "http://localhost/x/", "x",
			[("shared.bin", 42)]);
		WebDirectory level2 = CreateFinishedDirectory(level1, "http://localhost/x/y/", "y", []);
		WebDirectory level3 = CreateFinishedDirectory(level2, "http://localhost/x/y/z/", "z", []);

		// Four levels below level1, the exact same content as level1 shows up again.
		WebDirectory parsedChild = new(level3)
		{
			Url = "http://localhost/x/y/z/w/",
			Name = "w"
		};
		parsedChild.Files.Add(new WebFile { Url = "http://localhost/x/y/z/w/shared.bin", FileName = "shared.bin", FileSize = 42 });

		DirectoryParser.CheckParsedResults(parsedChild, "http://localhost/", checkParents: false);

		Assert.True(parsedChild.Error);
		Assert.Empty(parsedChild.Files);
	}

	[Fact]
	public void CheckParsedResults_DoesNotFlagDifferentContent()
	{
		WebDirectory root = CreateFinishedDirectory(null, "http://localhost/", "ROOT", []);
		WebDirectory a = CreateFinishedDirectory(root, "http://localhost/a/", "a",
			[("file1.bin", 100), ("file2.bin", 200)]);

		WebDirectory parsedChild = new(a)
		{
			Url = "http://localhost/a/b/",
			Name = "b"
		};
		parsedChild.Files.Add(new WebFile { Url = "http://localhost/a/b/other.bin", FileName = "other.bin", FileSize = 999 });

		DirectoryParser.CheckParsedResults(parsedChild, "http://localhost/", checkParents: false);

		Assert.False(parsedChild.Error);
		Assert.Single(parsedChild.Files);
	}

	[Fact]
	public void CheckParsedResults_DoesNotFlagSameNamesWithDifferentSizes()
	{
		WebDirectory root = CreateFinishedDirectory(null, "http://localhost/", "ROOT", []);
		WebDirectory a = CreateFinishedDirectory(root, "http://localhost/a/", "a",
			[("file1.bin", 100)]);

		// Same file name as the parent, but a different size - must not be treated as a loop.
		WebDirectory parsedChild = new(a)
		{
			Url = "http://localhost/a/a/",
			Name = "a"
		};
		parsedChild.Files.Add(new WebFile { Url = "http://localhost/a/a/file1.bin", FileName = "file1.bin", FileSize = 999 });

		DirectoryParser.CheckParsedResults(parsedChild, "http://localhost/", checkParents: false);

		Assert.False(parsedChild.Error);
		Assert.Single(parsedChild.Files);
	}

	[Fact]
	public void CheckParsedResults_FallsBackWhenAncestorFingerprintWasNotCached()
	{
		WebDirectory root = CreateFinishedDirectory(null, "http://localhost/", "ROOT", []);

		WebDirectory a = new(root)
		{
			Url = "http://localhost/a/",
			Name = "a",
			Finished = true
		};
		a.Files.Add(new WebFile { Url = "http://localhost/a/file1.bin", FileName = "file1.bin", FileSize = 100 });
		// Deliberately NOT setting a.ContentFingerprint, to exercise the ComputeContentFingerprint() fallback.

		WebDirectory parsedChild = new(a)
		{
			Url = "http://localhost/a/a/",
			Name = "a"
		};
		parsedChild.Files.Add(new WebFile { Url = "http://localhost/a/a/file1.bin", FileName = "file1.bin", FileSize = 100 });

		DirectoryParser.CheckParsedResults(parsedChild, "http://localhost/", checkParents: false);

		Assert.True(parsedChild.Error);
	}
}
