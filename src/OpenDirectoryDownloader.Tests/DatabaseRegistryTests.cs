using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Server;
using OpenDirectoryDownloader.Storage;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class DatabaseRegistryTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"odd-registry-tests-{Guid.NewGuid():N}");

	public DatabaseRegistryTests()
	{
		Directory.CreateDirectory(_directory);
	}

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	private static DatabaseRegistry CreateRegistry() => new(NullLogger.Instance, pollInterval: TimeSpan.FromHours(1), minRefreshInterval: TimeSpan.Zero);

	private async Task<string> CreateDatabaseAsync(string fileName, string rootUrl, params string[] fileNames)
	{
		string path = Path.Combine(_directory, fileName);

		await using ScanDatabase database = await ScanDatabase.CreateAsync(path);

		WebDirectory root = new(parentWebDirectory: null) { Url = rootUrl, Name = "ROOT", Finished = true };
		await database.BeginRunAsync(new ScanSession(root), rootUrl, isFreshScan: true);
		database.MirrorDirectory(root);

		foreach (string name in fileNames)
		{
			database.MirrorFile(new WebFile { Url = $"{rootUrl}{name}", FileName = name, FileSize = 100 }, root);
		}

		await database.FlushAsync(TestContext.Current.CancellationToken);

		return path;
	}

	[Fact]
	public async Task Directory_IsScanned_AndEachDatabaseIsCounted()
	{
		await CreateDatabaseAsync("a.sqlite", "https://a.example.com/", "one.txt", "two.txt");
		await CreateDatabaseAsync("b.sqlite", "https://b.example.com/", "three.iso");

		await using DatabaseRegistry registry = CreateRegistry();
		await registry.StartAsync([_directory], TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		Assert.True(registry.IsMulti);
		Assert.Equal(2, registry.Sessions.Count);

		DatabaseSummary a = registry.Sessions.Single(session => session.Path.EndsWith("a.sqlite")).Summary;
		Assert.Equal(DatabaseState.Ready, a.State);
		Assert.Equal("https://a.example.com/", a.RootUrl);
		Assert.True(a.StatsReady);
		Assert.Equal(2, a.TotalFiles);
		Assert.Equal(200, a.TotalSize);
	}

	[Fact]
	public async Task OneFile_IsNotTheOverview()
	{
		string path = await CreateDatabaseAsync("a.sqlite", "https://a.example.com/", "one.txt");

		await using DatabaseRegistry registry = CreateRegistry();
		await registry.StartAsync([path], TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		Assert.False(registry.IsMulti);
		Assert.NotNull(registry.Default);
		Assert.Equal(DatabaseState.Ready, registry.Default.Summary.State);
	}

	[Fact]
	public async Task ABrokenDatabase_IsReportedWithoutAffectingTheOthers()
	{
		await CreateDatabaseAsync("good.sqlite", "https://a.example.com/", "one.txt");
		await File.WriteAllTextAsync(Path.Combine(_directory, "broken.sqlite"), "this is not a database", TestContext.Current.CancellationToken);

		await using DatabaseRegistry registry = CreateRegistry();
		await registry.StartAsync([_directory], TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		Assert.Equal(DatabaseState.Ready, registry.Sessions.Single(session => session.Path.EndsWith("good.sqlite")).Summary.State);

		DatabaseSummary broken = registry.Sessions.Single(session => session.Path.EndsWith("broken.sqlite")).Summary;
		Assert.True(broken.State is DatabaseState.Error or DatabaseState.Incompatible);
		Assert.False(string.IsNullOrEmpty(broken.Error));
	}

	[Fact]
	public async Task AddedAndRemovedFiles_AreFoundWhenSyncing()
	{
		await CreateDatabaseAsync("a.sqlite", "https://a.example.com/", "one.txt");

		await using DatabaseRegistry registry = CreateRegistry();
		await registry.StartAsync([_directory], TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		long version = registry.Version;

		string added = await CreateDatabaseAsync("b.sqlite", "https://b.example.com/", "two.txt");
		await registry.SyncAsync(TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		Assert.Equal(2, registry.Sessions.Count);
		Assert.True(registry.Version > version);

		version = registry.Version;
		File.Delete(added);
		File.Delete($"{added}-wal");
		File.Delete($"{added}-shm");
		await registry.SyncAsync(TestContext.Current.CancellationToken);

		Assert.Single(registry.Sessions);
		Assert.True(registry.Version > version);
	}

	[Fact]
	public async Task AChangedDatabase_IsCountedAgain()
	{
		// A scan that is still writing
		string path = Path.Combine(_directory, "running.sqlite");
		await using ScanDatabase writer = await ScanDatabase.CreateAsync(path, TestContext.Current.CancellationToken);

		WebDirectory root = new(parentWebDirectory: null) { Url = "https://a.example.com/", Name = "ROOT" };
		await writer.BeginRunAsync(new ScanSession(root), root.Url, isFreshScan: true);
		writer.MirrorDirectory(root);
		writer.MirrorFile(new WebFile { Url = "https://a.example.com/one.txt", FileName = "one.txt", FileSize = 1 }, root);
		await writer.FlushAsync(TestContext.Current.CancellationToken);

		await using DatabaseRegistry registry = CreateRegistry();
		await registry.StartAsync([_directory], TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		Assert.Equal(1, registry.Sessions.Single().Summary.TotalFiles);
		Assert.Null(registry.Sessions.Single().Summary.CompletedAtUtc);

		writer.MirrorFile(new WebFile { Url = "https://a.example.com/extra.txt", FileName = "extra.txt", FileSize = 5 }, root);
		await writer.FlushAsync(TestContext.Current.CancellationToken);

		await registry.SyncAsync(TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		Assert.Equal(2, registry.Sessions.Single().Summary.TotalFiles);
	}

	[Fact]
	public async Task SearchAll_FindsMatchesPerDatabase_AndCapsEach()
	{
		await CreateDatabaseAsync("a.sqlite", "https://a.example.com/", "ubuntu-22.iso", "ubuntu-24.iso", "other.txt");
		await CreateDatabaseAsync("b.sqlite", "https://b.example.com/", "xubuntu.iso");
		await CreateDatabaseAsync("c.sqlite", "https://c.example.com/", "nothing.txt");

		await using DatabaseRegistry registry = CreateRegistry();
		await registry.StartAsync([_directory], TestContext.Current.CancellationToken);
		await registry.WaitForRefreshesAsync();

		List<DatabaseSearchResult> results = await registry.SearchAllAsync("ubuntu", 1, TestContext.Current.CancellationToken);

		Assert.Equal(3, results.Count);

		DatabaseSearchResult a = results.Single(result => result.Session.Path.EndsWith("a.sqlite"));
		Assert.Single(a.Result.Files);
		Assert.True(a.Result.Truncated);

		Assert.Single(results.Single(result => result.Session.Path.EndsWith("b.sqlite")).Result.Files);
		Assert.Empty(results.Single(result => result.Session.Path.EndsWith("c.sqlite")).Result.Files);
	}

	[Fact]
	public async Task MissingPath_IsReported()
	{
		await using DatabaseRegistry registry = CreateRegistry();
		List<string> problems = await registry.StartAsync([Path.Combine(_directory, "nope.sqlite")], TestContext.Current.CancellationToken);

		Assert.Single(problems);
		Assert.Empty(registry.Sessions);
	}
}
