using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Storage;
using Xunit;

namespace OpenDirectoryDownloader.Tests.Integration;

/// <summary>
/// End-to-end scans of a fake web server: the real crawler, real HTTP, real parsers, no external services.
/// Sizes are kept small so these run in every normal test run; the opt-in big ones live in LargeScanLoadTests.
/// </summary>
[Collection(ScanCollection.Name)]
public sealed class ScanIntegrationTests : IDisposable
{
	private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "odd-integration-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		try
		{
			Directory.Delete(_workingDirectory, recursive: true);
		}
		catch (IOException)
		{
			// Best effort: a lingering SQLite file handle must not fail the test
		}
	}

	private string DatabasePath => Path.Combine(_workingDirectory, "scan.sqlite");

	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	[InlineData(ListingStyle.HtmlTable)]
	public async Task Scan_FindsEveryDirectoryAndFile(ListingStyle style)
	{
		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = style,
			Depth = 2,
			SubdirectoriesPerDirectory = 3,
			FilesPerDirectory = 6
		});
		FakeSiteExpectation expected = server.Site.Expectation();

		ScanResult result = await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory);

		Session session = result.Session;
		Assert.Equal(expected.Files, session.Root.TotalFiles);
		Assert.Equal(expected.Directories, session.Root.TotalDirectories + 1);
		Assert.Equal(0, session.Errors);
		Assert.Equal(expected.Directories, server.DirectoryRequests);
		AssertTotalSize(style, expected.TotalBytes, session.Root.TotalFileSize);
	}

	/// <summary>nginx and the table list exact bytes; Apache rounds to 1.4G / 230K, so allow for that.</summary>
	private static void AssertTotalSize(ListingStyle style, long expectedBytes, long actualBytes)
	{
		if (style == ListingStyle.ApachePre)
		{
			Assert.InRange(actualBytes, (long)(expectedBytes * 0.95), (long)(expectedBytes * 1.05));
		}
		else
		{
			Assert.Equal(expectedBytes, actualBytes);
		}
	}

	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	public async Task Scan_OneDirectoryWithManyFiles(ListingStyle style)
	{
		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = style,
			Depth = 1,
			SubdirectoriesPerDirectory = 2,
			FilesPerDirectory = 10,
			FilesPerDirectoryOverride = path => path == "/dir-01/" ? 20_000 : null
		});
		FakeSiteExpectation expected = server.Site.Expectation();

		ScanResult result = await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory);

		Assert.Equal(expected.Files, result.Session.Root.TotalFiles);
		Assert.Equal(0, result.Session.Errors);
	}

	[Fact]
	public async Task Scan_WithDatabase_MirrorsEverythingAndAnEvictedScanFindsTheSame()
	{
		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = ListingStyle.NginxAutoindex,
			Depth = 3,
			SubdirectoriesPerDirectory = 3,
			FilesPerDirectory = 8
		});
		FakeSiteExpectation expected = server.Site.Expectation();

		await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory, "--use-database", "--keep-db", "--evict-memory", "--db-path", DatabasePath);

		await using ScanDatabase database = await ScanDatabase.OpenReadOnlyAsync(DatabasePath);
		Assert.Equal(expected.Files, await database.CountFilesAsync());
		Assert.Equal(expected.Directories, await database.CountDirectoriesAsync());
		Assert.Equal(expected.TotalBytes, (await database.GetExtensionStatsAsync()).Values.Sum(stats => stats.FileSize));
	}

	[Fact]
	public async Task Resume_WhenEverythingIsAlreadyScanned_AddsNoNewRunAndDoesNotRequestAnythingAgain()
	{
		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = ListingStyle.ApachePre,
			Depth = 2,
			SubdirectoriesPerDirectory = 2,
			FilesPerDirectory = 4
		});

		await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory, "--use-database", "--keep-db", "--db-path", DatabasePath);
		long requestsAfterFirstScan = server.DirectoryRequests;

		long runsBefore;

		await using (ScanDatabase before = await ScanDatabase.OpenReadOnlyAsync(DatabasePath))
		{
			runsBefore = await before.CountRunsAsync();
		}

		await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory, "--use-database", "--resume", "--db-path", DatabasePath);

		await using ScanDatabase after = await ScanDatabase.OpenReadOnlyAsync(DatabasePath);
		Assert.Equal(runsBefore, await after.CountRunsAsync());
		Assert.Equal(requestsAfterFirstScan, server.DirectoryRequests);
	}
}
