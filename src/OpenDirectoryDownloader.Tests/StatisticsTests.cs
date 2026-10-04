using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Storage;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class StatisticsTests : IAsyncLifetime
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"odd-statistics-tests-{Guid.NewGuid():N}.sqlite");
	private ScanDatabase _scanDatabase;

	public async Task InitializeAsync()
	{
		_scanDatabase = await ScanDatabase.CreateAsync(_dbPath);
	}

	public async Task DisposeAsync()
	{
		await _scanDatabase.DisposeAsync();

		foreach (string path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}

	[Fact]
	public async Task GetExtensionsAsync_GroupsByLowercasedExtensionIncludingTheDot()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a.TXT", FileName = "a.TXT", FileSize = 10 }, root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/b.txt", FileName = "b.txt", FileSize = 20 }, root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/c.zip", FileName = "c.zip", FileSize = 100 }, root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/noext", FileName = "noext", FileSize = 5 }, root);
		await _scanDatabase.FlushAsync();

		Dictionary<string, ExtensionStats> extensions = await Statistics.GetExtensionsAsync(_scanDatabase);

		Assert.Equal(3, extensions.Count);
		Assert.Equal(2, extensions[".txt"].Count);
		Assert.Equal(30, extensions[".txt"].FileSize);
		Assert.Equal(1, extensions[".zip"].Count);
		Assert.Equal(100, extensions[".zip"].FileSize);
		Assert.Equal(1, extensions[""].Count);
		Assert.Equal(5, extensions[""].FileSize);
	}
}
