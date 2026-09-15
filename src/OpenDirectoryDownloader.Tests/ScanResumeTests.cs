using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Storage;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// Regression coverage for issue #56 phase 6: ScanResume rebuilds an in-memory WebDirectory tree from a
/// previous run's scan database, restoring already-finished subtrees as evicted stubs (no Files ever
/// materialized) and only fully reconstructing the still-open frontier that needs to be requeued.
/// </summary>
public class ScanResumeTests : IAsyncLifetime
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"odd-resume-tests-{Guid.NewGuid():N}.sqlite");
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

	private void MirrorDirectory(string url, string parentUrl, bool finished, bool error = false)
	{
		WebDirectory parent = parentUrl is null ? null : new WebDirectory(null) { Url = parentUrl };
		WebDirectory directory = new(parent) { Url = url, Name = url.TrimEnd('/').Split('/')[^1], Finished = finished, Error = error };

		_scanDatabase.MirrorDirectory(directory);
	}

	private void MirrorFile(string url, string directoryUrl, string fileName, long? fileSize)
	{
		WebDirectory directory = new(null) { Url = directoryUrl };
		WebFile file = new() { Url = url, FileName = fileName, FileSize = fileSize };

		_scanDatabase.MirrorFile(file, directory);
	}

	[Fact]
	public async Task BuildAsync_UnknownRoot_ReturnsNull()
	{
		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false);

		Assert.Null(result);
	}

	[Fact]
	public async Task BuildAsync_FullyFinishedTree_RestoresRootAsClosedStub()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorDirectory("http://localhost/a/", "http://localhost/", finished: true);
		MirrorFile("http://localhost/a/f1.bin", "http://localhost/a/", "f1.bin", 100);
		MirrorFile("http://localhost/a/f2.bin", "http://localhost/a/", "f2.bin", 200);
		MirrorFile("http://localhost/f0.bin", "http://localhost/", "f0.bin", 50);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false);

		Assert.NotNull(result);
		Assert.True(result.Root.ContentEvicted);
		Assert.Empty(result.Root.Files);
		Assert.Empty(result.Root.Subdirectories);
		Assert.Empty(result.DirectoriesToRequeue);
		Assert.Empty(result.FilesToRequeueForSize);
		Assert.Equal(3, result.Root.CachedTotalFiles);
		Assert.Equal(350, result.Root.CachedTotalFileSize);
		Assert.Equal(1, result.Root.CachedTotalDirectories);
		Assert.Contains("http://localhost/", result.ProcessedUrls);
		Assert.Contains("http://localhost/a/", result.ProcessedUrls);
	}

	[Fact]
	public async Task BuildAsync_UnfinishedLeaf_RequeuesItAndLeavesAncestorsOpen()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorDirectory("http://localhost/done/", "http://localhost/", finished: true);
		MirrorFile("http://localhost/done/f1.bin", "http://localhost/done/", "f1.bin", 100);
		MirrorDirectory("http://localhost/pending/", "http://localhost/", finished: false);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false);

		Assert.NotNull(result);
		Assert.False(result.Root.ContentEvicted); // has an open descendant
		Assert.Single(result.DirectoriesToRequeue);
		Assert.Equal("http://localhost/pending/", result.DirectoriesToRequeue[0].Url);
		Assert.Equal(1, result.DirectoriesToRequeue[0].PendingWork);
		Assert.DoesNotContain("http://localhost/pending/", result.ProcessedUrls);

		// The finished sibling subtree should still have been restored as a closed stub.
		WebDirectory doneChild = Assert.Single(result.Root.Subdirectories, sd => sd.Url == "http://localhost/done/");
		Assert.True(doneChild.ContentEvicted);
		Assert.Equal(1, doneChild.CachedTotalFiles);

		WebDirectory pendingChild = Assert.Single(result.Root.Subdirectories, sd => sd.Url == "http://localhost/pending/");
		Assert.Same(result.DirectoriesToRequeue[0], pendingChild);
		Assert.Same(result.Root, pendingChild.ParentDirectory);
	}

	[Fact]
	public async Task BuildAsync_FinishedDirectoryWithUnresolvedFileSize_RequeuesFileAndStaysOpen()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorFile("http://localhost/f1.bin", "http://localhost/", "f1.bin", null);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false);

		Assert.NotNull(result);
		Assert.False(result.Root.ContentEvicted);
		Assert.Equal(1, result.Root.PendingWork);
		Assert.Single(result.FilesToRequeueForSize);
		Assert.Equal("http://localhost/f1.bin", result.FilesToRequeueForSize[0].Url);
		Assert.Same(result.Root, result.FilesToRequeueForSize[0].ParentDirectory);
	}

	[Fact]
	public async Task BuildAsync_SkipFileSizeLookups_TreatsNullSizesAsResolved()
	{
		// Mirrors an FTP scan, where file sizes are never looked up separately.
		MirrorDirectory("ftp://localhost/", null, finished: true);
		MirrorFile("ftp://localhost/f1.bin", "ftp://localhost/", "f1.bin", null);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "ftp://localhost/", skipFileSizeLookups: true);

		Assert.NotNull(result);
		Assert.True(result.Root.ContentEvicted);
		Assert.Empty(result.FilesToRequeueForSize);
	}

	[Fact]
	public async Task BuildAsync_DeepClosedSubtree_ComputesCachedTotalsAcrossMultipleLevels()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorDirectory("http://localhost/a/", "http://localhost/", finished: true);
		MirrorDirectory("http://localhost/a/b/", "http://localhost/a/", finished: true);
		MirrorFile("http://localhost/a/b/f1.bin", "http://localhost/a/b/", "f1.bin", 10);
		MirrorFile("http://localhost/a/b/f2.bin", "http://localhost/a/b/", "f2.bin", 20);
		MirrorFile("http://localhost/a/f3.bin", "http://localhost/a/", "f3.bin", 30);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false);

		Assert.True(result.Root.ContentEvicted);
		Assert.Equal(3, result.Root.CachedTotalFiles);
		Assert.Equal(60, result.Root.CachedTotalFileSize);
		Assert.Equal(2, result.Root.CachedTotalDirectories); // a/ and a/b/
	}

	[Fact]
	public async Task BuildAsync_ErroredFinishedDirectory_NotRetriedByDefault()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorDirectory("http://localhost/bad/", "http://localhost/", finished: true, error: true);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false);

		Assert.Empty(result.DirectoriesToRequeue);
		Assert.Contains("http://localhost/bad/", result.ProcessedUrls);

		// An errored-but-not-retried directory is still a "dead end" as far as closure is concerned - it
		// won't ever be revisited, so it (and therefore root, its only child) is eligible for eviction just
		// like any other finished leaf. CachedTotalDirectories still counts it (1: "bad").
		Assert.True(result.Root.ContentEvicted);
		Assert.Equal(1, result.Root.CachedTotalDirectories);
	}

	[Fact]
	public async Task BuildAsync_ErroredFinishedDirectory_RetriedWhenRequested()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorDirectory("http://localhost/bad/", "http://localhost/", finished: true, error: true);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false, retryErrors: true);

		WebDirectory requeued = Assert.Single(result.DirectoriesToRequeue);
		Assert.Equal("http://localhost/bad/", requeued.Url);
		Assert.False(requeued.Finished);
		Assert.False(requeued.Error); // reset for a clean attempt
		Assert.Equal(1, requeued.PendingWork);
		Assert.DoesNotContain("http://localhost/bad/", result.ProcessedUrls);

		// Retrying one errored leaf must not evict/close the root - it's now an open descendant again.
		Assert.False(result.Root.ContentEvicted);
	}

	[Fact]
	public async Task BuildAsync_RetryErrors_DoesNotAffectNonErroredDirectories()
	{
		MirrorDirectory("http://localhost/", null, finished: true);
		MirrorDirectory("http://localhost/good/", "http://localhost/", finished: true, error: false);
		MirrorFile("http://localhost/good/f1.bin", "http://localhost/good/", "f1.bin", 10);

		await _scanDatabase.FlushAsync();

		ScanResume.Result result = await ScanResume.BuildAsync(_scanDatabase, "http://localhost/", skipFileSizeLookups: false, retryErrors: true);

		Assert.Empty(result.DirectoriesToRequeue);
		Assert.True(result.Root.ContentEvicted);
	}
}
