using Microsoft.Extensions.Hosting;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Hosting;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Reporting;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Server;
using OpenDirectoryDownloader.Storage;
using OpenDirectoryDownloader.Urls;
using Serilog;
using Serilog.Extensions.Logging;
using System.CommandLine;
using System.Diagnostics;
using System.Text;

namespace OpenDirectoryDownloader;

public static class Program
{
	public static async Task<int> Main(string[] args)
	{
		ConsoleTitle consoleTitle = new();
		consoleTitle.Set("OpenDirectoryDownloader");

		Console.OutputEncoding = Encoding.UTF8;

		// Before anything looks up encodings, else codepages like windows-1251 are unknown
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

		ParseResult parseResult = CliDefinition.Parse(args);

		// Help, version, or a mistake on the command line
		if (parseResult.Action is not null || parseResult.Errors.Count > 0)
		{
			return await parseResult.InvokeAsync();
		}

		CliInvocation invocation = CliDefinition.Bind(parseResult);

		await using Serilog.Core.Logger logger = Hosting.Logging.CreateLogger();
		Log.Logger = logger;

		Console.WriteLine($"Started with PID {Environment.ProcessId}");
		logger.Information("Started with PID {processId}", Environment.ProcessId);

		try
		{
			return invocation.Mode switch
			{
				RunMode.Serve => await ServeAsync(invocation, logger),
				RunMode.Report => Report(invocation),
				_ => await ScanAsync(invocation, logger, consoleTitle)
			};
		}
		catch (Exception ex)
		{
			logger.Fatal(ex, "Unexpected error");
			Console.WriteLine($"Unexpected error: {ex.Message}");

			return 1;
		}
	}

	private static async Task<int> ScanAsync(CliInvocation invocation, Serilog.Core.Logger logger, ConsoleTitle consoleTitle)
	{
		string url = invocation.Crawl.Url;

		if (string.IsNullOrWhiteSpace(url))
		{
			Console.WriteLine("Which URL do you want to index?");
			url = Console.ReadLine();

			if (url?.Equals("quit", StringComparison.OrdinalIgnoreCase) == true || url?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true)
			{
				return 0;
			}
		}

		if (string.IsNullOrWhiteSpace(url))
		{
			Console.WriteLine("No URL specified");
			return 1;
		}

		// A session file typed at the prompt
		if (File.Exists(url))
		{
			return Report(invocation with { Crawl = invocation.Crawl with { Url = url } });
		}

		Console.WriteLine($"URL specified: {url}");

		string fixedUrl = UrlHelper.FixUrl(url);

		if (fixedUrl != url)
		{
			Console.WriteLine($"URL fixed    : {fixedUrl}");
		}

		using IHost host = await ScanApp.BuildAsync(invocation, fixedUrl, logger, interactiveConsole: true);

		consoleTitle.Set($"{new Uri(fixedUrl).Host.Replace("www.", string.Empty)} - {consoleTitle.Current}");

		ConsoleCommandService.ShowInfoAndCommands();
		Console.WriteLine("Started indexing!");

		await host.RunAsync();

		return 0;
	}

	/// <summary>Shows the statistics of a saved session (-u &lt;file.json&gt;).</summary>
	private static int Report(CliInvocation invocation)
	{
		SessionSummary summary = SessionJsonReader.ReadSummary(invocation.Crawl.Url);
		SessionHeader header = summary.Header;

		ScanSession session = new(new WebDirectory(null) { Name = Constants.Root, Url = header.Scan.RootUrl })
		{
			Started = header.Scan.Started,
			Finished = header.Scan.Finished ?? DateTimeOffset.MinValue,
			Description = header.Scan.Description,
			TotalFiles = summary.TotalFiles,
			TotalFileSizeEstimated = summary.TotalFileSize,
			UploadedUrlsUrl = header.Stats.UploadedUrlsUrl,
			SpeedtestResult = header.Stats.Speedtest is { } speedtest ? new SpeedtestResult
			{
				DownloadedBytes = speedtest.DownloadedBytes,
				ElapsedMilliseconds = speedtest.ElapsedMilliseconds,
				MaxBytesPerSecond = speedtest.MaxBytesPerSecond
			} : null
		};

		session.Counters.Restore(header.Stats.HttpRequests, header.Stats.HttpTraffic, header.Stats.Errors, header.Stats.Skipped, summary.TotalFiles, summary.TotalFileSize, summary.TotalDirectories);

		foreach ((int statusCode, int count) in header.Stats.HttpStatusCodes)
		{
			session.HttpStatusCodes.Add(statusCode, count);
		}

		Console.WriteLine(Statistics.GetSessionStats(session, new ScanReport(summary.Extensions), includeExtensions: true, totalDirectories: summary.TotalDirectories));

		if (!Console.IsInputRedirected)
		{
			Console.ReadKey(intercept: true);
		}

		return 0;
	}

	private static async Task<int> ServeAsync(CliInvocation invocation, Serilog.Core.Logger logger)
	{
		ServeOptions serve = invocation.Serve;

		using SerilogLoggerFactory loggerFactory = new(logger);

		return await ScanDatabaseServer.RunAsync(new ViewerOptions(serve.Database, serve.Port, serve.Host, serve.NoBrowser), loggerFactory.CreateLogger("OpenDirectoryDownloader.Server"));
	}
}
