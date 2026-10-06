using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Hosting;
using OpenDirectoryDownloader.Scanning;
using System.CommandLine;
using System.Diagnostics;
using Xunit;

namespace OpenDirectoryDownloader.Tests.Integration;

public sealed record ScanResult(
	ScanSession Session,
	string DatabasePath,
	TimeSpan Elapsed,
	long PeakWorkingSetBytes,
	long ManagedBytesAfter);

/// <summary>
/// Runs a real scan of a URL: the same host and services as the application (command line included), minus the
/// console keys and prompts. Scans run one at a time (<see cref="ScanCollection"/>) so their timings and memory
/// numbers don't disturb each other.
/// </summary>
public static class ScanRunner
{
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

		ParseResult parseResult = CliDefinition.Parse(arguments);
		Assert.True(parseResult.Errors.Count == 0, "Command line options did not parse: " + string.Join(' ', arguments) + " " + string.Join(", ", parseResult.Errors.Select(e => e.Message)));

		CliInvocation invocation = CliDefinition.Bind(parseResult);

		Process process = Process.GetCurrentProcess();
		Stopwatch stopwatch = Stopwatch.StartNew();

		using IHost host = await ScanApp.BuildAsync(invocation, Urls.UrlHelper.FixUrl(url), logger: null, interactiveConsole: false, TestContext.Current.CancellationToken);

		await host.StartAsync(TestContext.Current.CancellationToken);
		await host.Services.GetRequiredService<ScanController>().Completion.WaitAsync(TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);
		stopwatch.Stop();

		await host.StopAsync(TestContext.Current.CancellationToken);

		process.Refresh();

		string databasePath = host.Services.GetRequiredService<ScanDatabaseFile>().Path;

		return new ScanResult(
			host.Services.GetRequiredService<ScanSession>(),
			databasePath,
			stopwatch.Elapsed,
			process.PeakWorkingSet64,
			GC.GetTotalMemory(forceFullCollection: false));
	}
}

/// <summary>Scans run one at a time: they measure time and memory.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScanCollection
{
	public const string Name = "Scans";
}
