using Microsoft.Data.Sqlite;
using OpenDirectoryDownloader.Shared;
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
	public async Task GetFileInfoAsync_ReturnsTrueFileNameEvenWhenUrlDoesNotContainIt()
	{
		// Mirrors a Google Drive-style download URL, where the filename isn't in the URL at all - only
		// the database (from the original directory listing) knows it's really "Photo Album.zip".
		WebDirectory directory = new(parentWebDirectory: null)
		{
			Url = "https://drive.google.com/",
			Name = "drive.google.com",
			Finished = true
		};

		WebFile file = new()
		{
			Url = "https://drive.google.com/uc?id=abc123&export=download",
			FileName = "Photo Album.zip",
			FileSize = 12345
		};

		_scanDatabase.MirrorDirectory(directory);
		_scanDatabase.MirrorFile(file, directory);
		await _scanDatabase.FlushAsync();

		ScanDatabase.FileInfoSnapshot fileInfo = await _scanDatabase.GetFileInfoAsync(file.Url);

		Assert.NotNull(fileInfo);
		Assert.Equal("Photo Album.zip", fileInfo.FileName);
		Assert.Equal(12345, fileInfo.FileSize);
	}

	[Fact]
	public async Task GetFileInfoAsync_UnknownUrl_ReturnsNull()
	{
		Assert.Null(await _scanDatabase.GetFileInfoAsync("https://example.com/never-seen.zip"));
	}

	[Fact]
	public async Task GetDistinctParserTypesAsync_ReturnsDistinctSortedNonEmptyValues()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true, Parser = "ParseTablesDirectoryListing" };
		WebDirectory alist = new(parentWebDirectory: root) { Url = "https://example.com/alist/", Name = "alist", Finished = true, Parser = "AList" };
		WebDirectory dufs = new(parentWebDirectory: root) { Url = "https://example.com/dufs/", Name = "dufs", Finished = true, Parser = "Dufs" };
		WebDirectory anotherAlist = new(parentWebDirectory: root) { Url = "https://example.com/alist2/", Name = "alist2", Finished = true, Parser = "AList" };
		WebDirectory notParsedYet = new(parentWebDirectory: root) { Url = "https://example.com/pending/", Name = "pending", Finished = false, Parser = "" };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(alist);
		_scanDatabase.MirrorDirectory(dufs);
		_scanDatabase.MirrorDirectory(anotherAlist);
		_scanDatabase.MirrorDirectory(notParsedYet);
		await _scanDatabase.FlushAsync();

		List<string> types = await _scanDatabase.GetDistinctParserTypesAsync();

		Assert.Equal(["AList", "Dufs", "ParseTablesDirectoryListing"], types);
	}

	[Fact]
	public async Task GetOverallStatsAsync_SumsFilesAndSizeAcrossWholeDatabase()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		WebDirectory subdirectory = new(parentWebDirectory: root) { Url = "https://example.com/sub/", Name = "sub", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(subdirectory);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a.txt", FileName = "a.txt", FileSize = 100 }, root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/b.txt", FileName = "b.txt", FileSize = 250 }, root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/sub/c.txt", FileName = "c.txt", FileSize = null }, subdirectory);
		await _scanDatabase.FlushAsync();

		ScanDatabase.OverallStats stats = await _scanDatabase.GetOverallStatsAsync();

		Assert.Equal(3, stats.TotalFiles);
		Assert.Equal(350, stats.TotalSize);
		Assert.Equal(2, stats.TotalDirectories);
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
	public async Task GetLatestSpeedtestResultAsync_NoneRecorded_ReturnsNull()
	{
		Session session = new();
		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		Assert.Null(await _scanDatabase.GetLatestSpeedtestResultAsync());
	}

	[Fact]
	public async Task MirrorSpeedtestResult_ThenRead_RoundTripsAllFields()
	{
		Session session = new();
		await _scanDatabase.BeginRunAsync(session, "https://example.com/", isFreshScan: true);

		_scanDatabase.MirrorSpeedtestResult(new SpeedtestResult
		{
			DownloadedBytes = 10_485_760,
			ElapsedMilliseconds = 2000,
			MaxBytesPerSecond = 5_242_880
		});

		ScanDatabase.SpeedtestSnapshot snapshot = await _scanDatabase.GetLatestSpeedtestResultAsync();

		Assert.NotNull(snapshot);
		Assert.Equal(10_485_760, snapshot.DownloadedBytes);
		Assert.Equal(2000, snapshot.ElapsedMilliseconds);
		Assert.Equal(5_242_880, snapshot.MaxBytesPerSecond);
	}

	[Fact]
	public async Task MirrorSpeedtestResult_BeforeAnyRunStarted_IsNoOp()
	{
		_scanDatabase.MirrorSpeedtestResult(new SpeedtestResult { DownloadedBytes = 123, ElapsedMilliseconds = 456, MaxBytesPerSecond = 789 });
		await _scanDatabase.FlushAsync();

		Assert.Null(await _scanDatabase.GetLatestSpeedtestResultAsync());
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

	[Fact]
	public async Task OpenReadOnlyAsync_ExistingScanDatabase_CanReadDirectoriesAndFiles()
	{
		WebDirectory root = new(parentWebDirectory: null)
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

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorFile(file, root);
		await _scanDatabase.FlushAsync();

		await using ScanDatabase readOnlyDatabase = await ScanDatabase.OpenReadOnlyAsync(_dbPath);

		Assert.Equal(1, await readOnlyDatabase.CountDirectoriesAsync());
		Assert.Equal(1, await readOnlyDatabase.CountFilesAsync());
		Assert.Equal(1234, await readOnlyDatabase.GetFileSizeAsync(file.Url));
	}

	[Fact]
	public async Task OpenReadOnlyAsync_ClosedCheckpointedDatabase_DeletesSidecarFilesOnDispose()
	{
		string path = Path.Combine(Path.GetTempPath(), $"odd-scandb-sidecar-tests-{Guid.NewGuid():N}.sqlite");
		string walPath = $"{path}-wal";
		string shmPath = $"{path}-shm";

		try
		{
			await using (ScanDatabase writer = await ScanDatabase.CreateAsync(path))
			{
				writer.MirrorDirectory(new WebDirectory(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true });
				await writer.FlushAsync();
			}

			// A cleanly closed writer checkpoints and SQLite removes its own WAL/SHM - nothing left behind
			// before the read-only connection in this test ever touches the file.
			Assert.False(File.Exists(walPath));
			Assert.False(File.Exists(shmPath));

			await using (ScanDatabase readOnlyDatabase = await ScanDatabase.OpenReadOnlyAsync(path))
			{
				Assert.Equal(1, await readOnlyDatabase.CountDirectoriesAsync());

				// Opening a read-only connection to a WAL-mode database creates these purely as SQLite's own
				// WAL-reader bookkeeping, even though nothing is ever written.
				Assert.True(File.Exists(walPath));
				Assert.True(File.Exists(shmPath));
			}

			Assert.False(File.Exists(walPath));
			Assert.False(File.Exists(shmPath));
		}
		finally
		{
			foreach (string candidate in new[] { path, walPath, shmPath })
			{
				if (File.Exists(candidate))
				{
					File.Delete(candidate);
				}
			}
		}
	}

	[Fact]
	public async Task OpenReadOnlyAsync_SidecarFilesAlreadyExistFromAnActiveWriter_LeavesThemAloneOnDispose()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		_scanDatabase.MirrorDirectory(root);
		await _scanDatabase.FlushAsync();

		string walPath = $"{_dbPath}-wal";
		string shmPath = $"{_dbPath}-shm";

		// _scanDatabase (this test class's writer fixture) is still open at this point, so these already
		// exist - they belong to that writer, not to the read-only connection opened below.
		Assert.True(File.Exists(walPath));
		Assert.True(File.Exists(shmPath));

		await using (ScanDatabase readOnlyDatabase = await ScanDatabase.OpenReadOnlyAsync(_dbPath))
		{
			Assert.Equal(1, await readOnlyDatabase.CountDirectoriesAsync());
		}

		// Must still be there - the still-open writer needs them, regardless of what the read-only
		// connection that just closed did or didn't create.
		Assert.True(File.Exists(walPath));
		Assert.True(File.Exists(shmPath));
	}

	[Fact]
	public async Task OpenReadOnlyAsync_MissingFile_Throws()
	{
		string missingPath = Path.Combine(Path.GetTempPath(), $"odd-missing-{Guid.NewGuid():N}.sqlite");

		await Assert.ThrowsAsync<FileNotFoundException>(() => ScanDatabase.OpenReadOnlyAsync(missingPath));
	}

	[Fact]
	public async Task OpenReadOnlyAsync_NotAScanDatabase_Throws()
	{
		string notAScanDbPath = Path.Combine(Path.GetTempPath(), $"odd-not-a-scan-db-{Guid.NewGuid():N}.sqlite");

		using (SqliteConnection connection = new($"Data Source={notAScanDbPath}"))
		{
			await connection.OpenAsync();

			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "CREATE TABLE SomethingElse (Id INTEGER PRIMARY KEY)";
			await command.ExecuteNonQueryAsync();

			SqliteConnection.ClearPool(connection);
		}

		try
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() => ScanDatabase.OpenReadOnlyAsync(notAScanDbPath));
		}
		finally
		{
			File.Delete(notAScanDbPath);
		}
	}

	[Fact]
	public async Task GetChildSubtreeAggregatesAsync_ComputesRecursiveTotalsPerImmediateChild()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		WebDirectory childA = new(parentWebDirectory: root) { Url = "https://example.com/a/", Name = "a", Finished = true };
		WebDirectory childAB = new(parentWebDirectory: childA) { Url = "https://example.com/a/b/", Name = "b", Finished = true };
		WebDirectory childC = new(parentWebDirectory: root) { Url = "https://example.com/c/", Name = "c", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(childA);
		_scanDatabase.MirrorDirectory(childAB);
		_scanDatabase.MirrorDirectory(childC);

		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a/x.bin", FileName = "x.bin", FileSize = 100 }, childA);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a/b/y.bin", FileName = "y.bin", FileSize = 200 }, childAB);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/c/z.bin", FileName = "z.bin", FileSize = 50 }, childC);

		await _scanDatabase.FlushAsync();

		Dictionary<string, ScanDatabase.SubtreeAggregate> aggregates = await _scanDatabase.GetChildSubtreeAggregatesAsync("https://example.com/");

		Assert.Equal(2, aggregates.Count);

		ScanDatabase.SubtreeAggregate aAggregate = aggregates["https://example.com/a/"];
		Assert.Equal(2, aAggregate.DirectoryCount);
		Assert.Equal(2, aAggregate.FileCount);
		Assert.Equal(300, aAggregate.TotalSize);

		ScanDatabase.SubtreeAggregate cAggregate = aggregates["https://example.com/c/"];
		Assert.Equal(1, cAggregate.DirectoryCount);
		Assert.Equal(1, cAggregate.FileCount);
		Assert.Equal(50, cAggregate.TotalSize);
	}

	[Fact]
	public async Task GetSubtreeAsync_ReturnsDescendantDirectoriesAndAllFilesUnderneath()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		WebDirectory childA = new(parentWebDirectory: root) { Url = "https://example.com/a/", Name = "a", Finished = true };
		WebDirectory childAB = new(parentWebDirectory: childA) { Url = "https://example.com/a/b/", Name = "b", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(childA);
		_scanDatabase.MirrorDirectory(childAB);

		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/root.bin", FileName = "root.bin", FileSize = 10 }, root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a/x.bin", FileName = "x.bin", FileSize = 100 }, childA);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a/b/y.bin", FileName = "y.bin", FileSize = 200 }, childAB);

		await _scanDatabase.FlushAsync();

		ScanDatabase.SubtreeResult subtree = await _scanDatabase.GetSubtreeAsync("https://example.com/");

		Assert.Equal(2, subtree.Directories.Count);
		Assert.Contains(subtree.Directories, d => d.Url == "https://example.com/a/");
		Assert.Contains(subtree.Directories, d => d.Url == "https://example.com/a/b/");
		Assert.DoesNotContain(subtree.Directories, d => d.Url == "https://example.com/");

		Assert.Equal(3, subtree.Files.Count);
		Assert.Contains(subtree.Files, f => f.Url == "https://example.com/root.bin" && f.DirectoryUrl == "https://example.com/");
		Assert.Contains(subtree.Files, f => f.Url == "https://example.com/a/x.bin" && f.DirectoryUrl == "https://example.com/a/");
		Assert.Contains(subtree.Files, f => f.Url == "https://example.com/a/b/y.bin" && f.DirectoryUrl == "https://example.com/a/b/");
	}

	[Fact]
	public async Task GetAncestorsAsync_DeepDirectory_ReturnsChainFromRootToImmediateParent()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		WebDirectory childA = new(parentWebDirectory: root) { Url = "https://example.com/a/", Name = "a", Finished = true };
		WebDirectory childAB = new(parentWebDirectory: childA) { Url = "https://example.com/a/b/", Name = "b", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(childA);
		_scanDatabase.MirrorDirectory(childAB);
		await _scanDatabase.FlushAsync();

		List<(string Url, string Name)> ancestors = await _scanDatabase.GetAncestorsAsync("https://example.com/a/b/");

		Assert.Equal(2, ancestors.Count);
		Assert.Equal(("https://example.com/", "example.com"), ancestors[0]);
		Assert.Equal(("https://example.com/a/", "a"), ancestors[1]);
	}

	[Fact]
	public async Task GetAncestorsAsync_Root_ReturnsEmptyList()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		await _scanDatabase.FlushAsync();

		Assert.Empty(await _scanDatabase.GetAncestorsAsync("https://example.com/"));
	}

	[Fact]
	public async Task GetAncestorsAsync_UnknownUrl_ReturnsEmptyList()
	{
		Assert.Empty(await _scanDatabase.GetAncestorsAsync("https://example.com/never-seen/"));
	}

	private async Task SeedSearchFixtureAsync()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		WebDirectory photos = new(parentWebDirectory: root) { Url = "https://example.com/photos/", Name = "photos", Finished = true };
		WebDirectory documents = new(parentWebDirectory: root) { Url = "https://example.com/documents/", Name = "documents", Finished = true };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(photos);
		_scanDatabase.MirrorDirectory(documents);

		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/photos/Holiday-Photo.jpg", FileName = "Holiday-Photo.jpg", FileSize = 100 }, photos);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/documents/report.pdf", FileName = "report.pdf", FileSize = 200 }, documents);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/documents/100%done.txt", FileName = "100%done.txt", FileSize = 5 }, documents);

		await _scanDatabase.FlushAsync();
	}

	[Fact]
	public async Task SearchAsync_MatchesFileNameSubstringCaseInsensitively()
	{
		await SeedSearchFixtureAsync();

		ScanDatabase.SearchResult result = await _scanDatabase.SearchAsync("holiday", 200);

		ScanDatabase.SearchFileMatch match = Assert.Single(result.Files);
		Assert.Equal("Holiday-Photo.jpg", match.FileName);
		Assert.Equal("https://example.com/photos/", match.DirectoryUrl);
		Assert.Equal(100, match.FileSize);
		Assert.Empty(result.Directories);
		Assert.False(result.Truncated);
	}

	[Fact]
	public async Task SearchAsync_MatchesDirectoryNameSubstring()
	{
		await SeedSearchFixtureAsync();

		ScanDatabase.SearchResult result = await _scanDatabase.SearchAsync("photo", 200);

		// Matches both the "photos" directory and "Holiday-Photo.jpg".
		Assert.Contains(result.Directories, d => d.Name == "photos");
		Assert.Contains(result.Files, f => f.FileName == "Holiday-Photo.jpg");
	}

	[Fact]
	public async Task SearchAsync_DirectoryMatch_IncludesFinishedAndErrorState()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		WebDirectory brokenFolder = new(parentWebDirectory: root) { Url = "https://example.com/broken-folder/", Name = "broken-folder", Finished = true, Error = true };
		WebDirectory pendingFolder = new(parentWebDirectory: root) { Url = "https://example.com/pending-folder/", Name = "pending-folder", Finished = false };

		_scanDatabase.MirrorDirectory(root);
		_scanDatabase.MirrorDirectory(brokenFolder);
		_scanDatabase.MirrorDirectory(pendingFolder);
		await _scanDatabase.FlushAsync();

		ScanDatabase.SearchResult result = await _scanDatabase.SearchAsync("folder", 200);

		ScanDatabase.SearchDirectoryMatch broken = Assert.Single(result.Directories, d => d.Name == "broken-folder");
		Assert.True(broken.Finished);
		Assert.True(broken.Error);

		ScanDatabase.SearchDirectoryMatch pending = Assert.Single(result.Directories, d => d.Name == "pending-folder");
		Assert.False(pending.Finished);
		Assert.False(pending.Error);
	}

	[Fact]
	public async Task SearchAsync_NoMatches_ReturnsEmptyResult()
	{
		await SeedSearchFixtureAsync();

		ScanDatabase.SearchResult result = await _scanDatabase.SearchAsync("nonexistent", 200);

		Assert.Empty(result.Files);
		Assert.Empty(result.Directories);
		Assert.False(result.Truncated);
	}

	[Fact]
	public async Task SearchAsync_LimitExceeded_SetsTruncatedAndCapsResults()
	{
		WebDirectory root = new(parentWebDirectory: null) { Url = "https://example.com/", Name = "example.com", Finished = true };
		_scanDatabase.MirrorDirectory(root);

		for (int i = 0; i < 5; i++)
		{
			_scanDatabase.MirrorFile(new WebFile { Url = $"https://example.com/match-{i}.bin", FileName = $"match-{i}.bin", FileSize = i }, root);
		}

		await _scanDatabase.FlushAsync();

		ScanDatabase.SearchResult result = await _scanDatabase.SearchAsync("match", 3);

		Assert.Equal(3, result.Files.Count);
		Assert.True(result.Truncated);
	}

	[Fact]
	public async Task SearchAsync_LiteralPercentAndUnderscore_AreNotTreatedAsWildcards()
	{
		await SeedSearchFixtureAsync();

		ScanDatabase.SearchResult literalPercentResult = await _scanDatabase.SearchAsync("100%done", 200);
		Assert.Single(literalPercentResult.Files);

		// "report_pdf" (underscore) should NOT match "report.pdf" - if '_' were treated as the SQL
		// single-character wildcard instead of a literal, it would.
		ScanDatabase.SearchResult literalUnderscoreResult = await _scanDatabase.SearchAsync("report_pdf", 200);
		Assert.Empty(literalUnderscoreResult.Files);
	}
}
