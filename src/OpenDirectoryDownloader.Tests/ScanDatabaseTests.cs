using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Storage;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class ScanDatabaseTests : IAsyncLifetime
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"odd-scandb-tests-{Guid.NewGuid():N}.sqlite");
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
	public async Task CreateAsync_CreatesEmptyDatabase()
	{
		Assert.True(File.Exists(_dbPath));
		Assert.Equal(0, await _scanDatabase.CountDirectoriesAsync());
		Assert.Equal(0, await _scanDatabase.CountFilesAsync());
	}

	[Fact]
	public async Task MirrorDirectory_PersistsDirectory()
	{
		WebDirectory root = new(parentWebDirectory: null)
		{
			Url = "https://example.com/",
			Name = "example.com",
			Finished = true
		};

		WebDirectory child = new(parentWebDirectory: root)
		{
			Url = "https://example.com/sub/",
			Name = "sub",
			Finished = true
		};

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(child);

		await _scanDatabase.FlushAsync();

		Assert.Equal(2, await _scanDatabase.CountDirectoriesAsync());
	}

	[Fact]
	public async Task MirrorDirectory_SameUrlTwice_UpsertsInsteadOfDuplicating()
	{
		WebDirectory root = new(parentWebDirectory: null)
		{
			Url = "https://example.com/",
			Name = "example.com",
			Finished = false
		};

		_scanDatabase.MirrorDirectory(root);
		await _scanDatabase.FlushAsync();
		Assert.Equal(1, await _scanDatabase.CountDirectoriesAsync());

		// Simulate the directory being reparsed/finished later - same Url, updated fields.
		root.Finished = true;
		root.Description = "done";

		_scanDatabase.MirrorDirectory(root);
		await _scanDatabase.FlushAsync();

		Assert.Equal(1, await _scanDatabase.CountDirectoriesAsync());
	}

	[Fact]
	public async Task MirrorFile_PersistsFileUnderItsDirectory()
	{
		WebDirectory directory = new(parentWebDirectory: null)
		{
			Url = "https://example.com/",
			Name = "example.com",
			Finished = true
		};

		WebFile file = new()
		{
			Url = "https://example.com/file.zip",
			FileName = "file.zip",
			FileSize = 1234
		};

		_scanDatabase.MirrorDirectory(directory);
		_scanDatabase.MirrorFile(file, directory);

		await _scanDatabase.FlushAsync();

		Assert.Equal(1, await _scanDatabase.CountFilesAsync());
		Assert.Equal(1234, await _scanDatabase.GetFileSizeAsync(file.Url));
	}

	[Fact]
	public async Task MirrorFileSize_UpdatesPreviouslyMirroredFile()
	{
		WebDirectory directory = new(parentWebDirectory: null)
		{
			Url = "https://example.com/",
			Name = "example.com",
			Finished = true
		};

		WebFile file = new()
		{
			Url = "https://example.com/file.zip",
			FileName = "file.zip",
			FileSize = null
		};

		_scanDatabase.MirrorDirectory(directory);
		_scanDatabase.MirrorFile(file, directory);
		await _scanDatabase.FlushAsync();

		Assert.Null(await _scanDatabase.GetFileSizeAsync(file.Url));

		file.FileSize = 42;
		_scanDatabase.MirrorFileSize(file);
		await _scanDatabase.FlushAsync();

		Assert.Equal(42, await _scanDatabase.GetFileSizeAsync(file.Url));
	}

	[Fact]
	public async Task FlushAsync_WaitsForAllPreviouslyQueuedWrites()
	{
		WebDirectory directory = new(parentWebDirectory: null)
		{
			Url = "https://example.com/",
			Name = "example.com",
			Finished = true
		};

		_scanDatabase.MirrorDirectory(directory);

		for (int i = 0; i < 2500; i++)
		{
			WebFile file = new()
			{
				Url = $"https://example.com/file-{i}.bin",
				FileName = $"file-{i}.bin",
				FileSize = i
			};

			_scanDatabase.MirrorFile(file, directory);
		}

		await _scanDatabase.FlushAsync();

		Assert.Equal(2500, await _scanDatabase.CountFilesAsync());
	}

	[Fact]
	public async Task GetLatestRunStatsAsync_NoRunStartedYet_ReturnsNull()
	{
		Assert.Null(await _scanDatabase.GetLatestRunStatsAsync());
	}

	[Fact]
	public async Task GetScanInfoAsync_NoRunStartedYet_ReturnsNull()
	{
		Assert.Null(await _scanDatabase.GetScanInfoAsync());
	}

	[Fact]
	public async Task BeginRunAsync_FreshScan_RecordsScanInfoAndFirstRun()
	{
		Session session = new();

		int runNumber = await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		Assert.Equal(1, runNumber);

		ScanDatabase.ScanInfoSnapshot scanInfo = await _scanDatabase.GetScanInfoAsync();
		Assert.NotNull(scanInfo);
		Assert.Equal("https://example.com/", scanInfo.RootUrl);
		Assert.Null(scanInfo.CompletedAtUtc);

		ScanDatabase.SessionStatsSnapshot latestRun = await _scanDatabase.GetLatestRunStatsAsync();
		Assert.NotNull(latestRun);
		Assert.Equal(1, latestRun.RunNumber);
		Assert.Equal(0, latestRun.TotalHttpRequests);
	}

	[Fact]
	public async Task MirrorSessionStats_ThenRead_RoundTripsAllFieldsForTheCurrentRun()
	{
		Session session = new();
		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		session.TotalHttpTraffic = 12345;
		session.TotalHttpRequests = 42;
		session.Errors = 3;
		session.Skipped = 1;
		session.HttpStatusCodes[200] = 40;
		session.HttpStatusCodes[404] = 2;

		_scanDatabase.MirrorSessionStats(session);

		ScanDatabase.SessionStatsSnapshot snapshot = await _scanDatabase.GetLatestRunStatsAsync();

		Assert.NotNull(snapshot);
		Assert.Equal(1, snapshot.RunNumber);
		Assert.Equal(12345, snapshot.TotalHttpTraffic);
		Assert.Equal(42, snapshot.TotalHttpRequests);
		Assert.Equal(3, snapshot.Errors);
		Assert.Equal(1, snapshot.Skipped);
		Assert.Equal(40, snapshot.HttpStatusCodes[200]);
		Assert.Equal(2, snapshot.HttpStatusCodes[404]);
	}

	[Fact]
	public async Task MirrorSessionStats_CalledAgain_OverwritesTheCurrentRunsSnapshot()
	{
		Session session = new();
		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		session.TotalHttpTraffic = 100;
		session.TotalHttpRequests = 1;
		_scanDatabase.MirrorSessionStats(session);

		session.TotalHttpTraffic = 999;
		session.TotalHttpRequests = 9;
		_scanDatabase.MirrorSessionStats(session);

		ScanDatabase.SessionStatsSnapshot snapshot = await _scanDatabase.GetLatestRunStatsAsync();

		Assert.Equal(999, snapshot.TotalHttpTraffic);
		Assert.Equal(9, snapshot.TotalHttpRequests);
	}

	[Fact]
	public async Task BeginRunAsync_CalledAgain_StartsANewRunCarryingForwardTheSessionsCurrentCounters()
	{
		Session session = new();
		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		session.TotalHttpRequests = 50;
		_scanDatabase.MirrorSessionStats(session);

		// Simulates a --resume: the caller restores Session's counters from the previous run before
		// calling BeginRunAsync again, exactly as OpenDirectoryIndexer does.
		session.TotalHttpRequests = 50;
		session.TotalHttpRequests += 25; // this run's own progress so far, on top of the carried-over value

		int secondRunNumber = await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: false);

		Assert.Equal(2, secondRunNumber);
		Assert.Equal(2, await _scanDatabase.CountRunsAsync());

		ScanDatabase.SessionStatsSnapshot latestRun = await _scanDatabase.GetLatestRunStatsAsync();
		Assert.Equal(2, latestRun.RunNumber);
		Assert.Equal(75, latestRun.TotalHttpRequests);

		// A second BeginRunAsync must not touch the one-time ScanInfo row (still the original root URL).
		ScanDatabase.ScanInfoSnapshot scanInfo = await _scanDatabase.GetScanInfoAsync();
		Assert.Equal("https://example.com/", scanInfo.RootUrl);
	}

	[Fact]
	public async Task CountRunsAsync_ReflectsNumberOfRunsStartedSoFar()
	{
		Session session = new();

		Assert.Equal(0, await _scanDatabase.CountRunsAsync());

		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);
		Assert.Equal(1, await _scanDatabase.CountRunsAsync());

		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: false);
		Assert.Equal(2, await _scanDatabase.CountRunsAsync());
	}

	[Fact]
	public async Task MarkCompleted_SetsScanInfoCompletedAtUtc()
	{
		Session session = new();
		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		DateTimeOffset completedAt = DateTimeOffset.UtcNow;
		_scanDatabase.MarkCompleted(completedAt);
		await _scanDatabase.FlushAsync();

		ScanDatabase.ScanInfoSnapshot scanInfo = await _scanDatabase.GetScanInfoAsync();

		Assert.NotNull(scanInfo.CompletedAtUtc);
		Assert.Equal(completedAt, scanInfo.CompletedAtUtc.Value, TimeSpan.FromSeconds(1));
	}
}
