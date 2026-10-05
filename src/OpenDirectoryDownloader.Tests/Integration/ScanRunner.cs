using CommandLine;
using OpenDirectoryDownloader.Shared.Models;
using Serilog;
using System.Diagnostics;
using Xunit;

namespace OpenDirectoryDownloader.Tests.Integration;

public sealed record ScanResult(
	Session Session,
	OpenDirectoryIndexer Indexer,
	string DatabasePath,
	TimeSpan Elapsed,
	long PeakWorkingSetBytes,
	long ManagedBytesAfter);

/// <summary>
/// Runs the real <see cref="OpenDirectoryIndexer"/> (the same code path as Program.Main, minus the console
/// prompts) against a URL. The indexer keeps its session in a static, so scans must never run in parallel:
/// every test class using this belongs to <see cref="ScanCollection"/>.
/// </summary>
public static class ScanRunner
{
	static ScanRunner()
	{
		// Program.Main normally sets these up; the tests don't want log files, only silence
		Program.Logger ??= new LoggerConfiguration().CreateLogger();
		Program.HistoryLogger ??= new LoggerConfiguration().CreateLogger();
	}

	public static async Task<ScanResult> RunAsync(string url, string workingDirectory, params string[] extraArguments)
	{
		Directory.CreateDirectory(workingDirectory);

		string outputBase = Path.Combine(workingDirectory, "scan");

		// Parsed (not constructed) so every option gets its documented default, same as the real command line.
		// "--no-urls" keeps the run from writing the URL list; the database is the thing we inspect afterwards.
		List<string> arguments =
		[
			"--url", url,
			"--no-urls",
			"--output-file", outputBase,
			.. extraArguments
		];

		CommandLineOptions options = null;
		Parser.Default.ParseArguments<CommandLineOptions>(arguments).WithParsed(o => options = o);
		Assert.True(options is not null, "Command line options did not parse: " + string.Join(' ', arguments));

		OpenDirectoryIndexerSettings settings = new()
		{
			Url = Library.FixUrl(url),
			CommandLineOptions = options,
			Threads = options.Threads,
			Timeout = options.Timeout
		};

		OpenDirectoryIndexer indexer = new(settings);

		Process process = Process.GetCurrentProcess();
		Stopwatch stopwatch = Stopwatch.StartNew();

		indexer.StartIndexingAsync();

		// StartIndexingAsync is async void: IndexingTask only exists once it has got past its database setup
		for (int attempt = 0; attempt < 600 && indexer.IndexingTask is null; attempt++)
		{
			await Task.Delay(50);
		}

		Assert.NotNull(indexer.IndexingTask);
		await indexer.IndexingTask.WaitAsync(TimeSpan.FromMinutes(30));
		stopwatch.Stop();

		process.Refresh();

		string databasePath = options.DbPath ?? outputBase + ".sqlite";

		return new ScanResult(
			OpenDirectoryIndexer.Session,
			indexer,
			databasePath,
			stopwatch.Elapsed,
			process.PeakWorkingSet64,
			GC.GetTotalMemory(forceFullCollection: false));
	}
}

/// <summary>The indexer's session is static, so scan tests share one collection and never run concurrently.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScanCollection
{
	public const string Name = "Scans";
}
