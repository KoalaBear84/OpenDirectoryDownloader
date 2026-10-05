using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace OpenDirectoryDownloader.Tests.Integration;

/// <summary>
/// A [Fact] that is skipped unless the environment variable ODD_LOAD_TESTS is 1. Load tests are slow and
/// memory hungry, so they never run in a normal 'dotnet test' or in Visual Studio's Test Explorer; ask for them explicitly.
/// </summary>
public sealed class LoadFactAttribute : FactAttribute
{
	public const string EnableVariable = "ODD_LOAD_TESTS";

	public LoadFactAttribute()
	{
		if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
		{
			Skip = $"Load test, skipped by default. Set {EnableVariable}=1 to run it (see LargeScanLoadTests).";
		}
	}
}

/// <summary>
/// Opt-in scans of very large fake sites, meant for comparing performance and memory between commits.
/// <code>
/// $env:ODD_LOAD_TESTS = "1"
/// dotnet test src/OpenDirectoryDownloader.Tests --filter "FullyQualifiedName~LargeScanLoadTests" --logger "console;verbosity=detailed"
/// </code>
/// Sizes: ODD_LOAD_FILES (files in the one huge directory, default 100000; the app treats a response over 20 MB, roughly 150000 files, as a file instead of a directory) and ODD_LOAD_DEPTH (depth of the
/// wide tree, default 4; 8 subdirectories per level and 40 files per directory). Each test prints elapsed time,
/// throughput and the process' peak working set. The numbers include the fake server running in the same
/// process, so only compare them between runs of the same scenario, not against a real scan.
/// </summary>
[Collection(ScanCollection.Name)]
public sealed class LargeScanLoadTests(ITestOutputHelper output) : IDisposable
{
	private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "odd-load-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		try
		{
			Directory.Delete(_workingDirectory, recursive: true);
		}
		catch (IOException)
		{
			// Best effort
		}
	}

	private static int EnvironmentInt(string name, int fallback) =>
		int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : fallback;

	private void Report(string scenario, FakeDirectoryServer server, ScanResult result, FakeSiteExpectation expected)
	{
		double seconds = result.Elapsed.TotalSeconds;

		output.WriteLine($"""
			== {scenario}
			Files: {result.Session.Root.TotalFiles:N0} (expected {expected.Files:N0}), directories: {expected.Directories:N0}
			Elapsed: {seconds:F1} s, {expected.Files / Math.Max(seconds, 0.001):N0} files/s, {server.DirectoryRequests:N0} directory requests
			Server: peak {server.PeakConcurrentRequests} requests at once, {server.AverageHandlerMilliseconds:F2} ms average per request
			Peak working set: {result.PeakWorkingSetBytes / 1024d / 1024d:N0} MB, managed heap after: {result.ManagedBytesAfter / 1024d / 1024d:N0} MB
			Errors: {result.Session.Errors}
			""");
	}

	[LoadFact]
	public async Task OneHugeDirectory()
	{
		int files = EnvironmentInt("ODD_LOAD_FILES", 100_000);

		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = ListingStyle.NginxAutoindex,
			Depth = 0,
			FilesPerDirectory = files,
			CacheListings = true
		});
		FakeSiteExpectation expected = server.Site.Expectation();

		ScanResult result = await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory);

		Report($"One directory with {files:N0} files", server, result, expected);
		Assert.Equal(expected.Files, result.Session.Root.TotalFiles);
	}

	[LoadFact]
	public async Task WideAndDeepTree()
	{
		int depth = EnvironmentInt("ODD_LOAD_DEPTH", 4);

		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = ListingStyle.ApachePre,
			Depth = depth,
			SubdirectoriesPerDirectory = 8,
			FilesPerDirectory = 40
		});
		FakeSiteExpectation expected = server.Site.Expectation();

		ScanResult result = await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory, "--threads", "20");

		Report($"Tree of depth {depth}, 8 subdirectories and 40 files per directory", server, result, expected);
		Assert.Equal(expected.Files, result.Session.Root.TotalFiles);
	}

	[LoadFact]
	public async Task WideAndDeepTree_WithDatabaseAndEviction()
	{
		int depth = EnvironmentInt("ODD_LOAD_DEPTH", 4);

		await using FakeDirectoryServer server = await FakeDirectoryServer.StartAsync(new FakeSiteOptions
		{
			Style = ListingStyle.HtmlTable,
			Depth = depth,
			SubdirectoriesPerDirectory = 8,
			FilesPerDirectory = 40
		});
		FakeSiteExpectation expected = server.Site.Expectation();

		ScanResult result = await ScanRunner.RunAsync(server.BaseUrl, _workingDirectory, "--threads", "20", "--use-database", "--evict-memory", "--db-path", Path.Combine(_workingDirectory, "load.sqlite"));

		Report($"Same tree with --use-database --evict-memory (depth {depth})", server, result, expected);
		Assert.Equal(expected.Files, result.Session.Root.TotalFiles);
	}
}
