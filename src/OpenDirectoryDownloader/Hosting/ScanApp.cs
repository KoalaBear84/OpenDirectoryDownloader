using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Browser;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Crawling;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Outputs;
using OpenDirectoryDownloader.Parsing;
using OpenDirectoryDownloader.Protocols;
using OpenDirectoryDownloader.Protocols.Ftp;
using OpenDirectoryDownloader.Protocols.Http;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using Serilog;

namespace OpenDirectoryDownloader.Hosting;

/// <summary>The composition of a scan: which service is what, built once per scan.</summary>
public static class ScanApp
{
	/// <summary>
	/// Builds the host for scanning <paramref name="url"/> (already normalized). The scan database is opened here,
	/// before the host exists, because the crawler cannot work without it.
	/// </summary>
	/// <param name="logger">The Serilog logger; null for none (tests)</param>
	public static async Task<IHost> BuildAsync(CliInvocation invocation, string url, Serilog.ILogger logger, bool interactiveConsole, CancellationToken cancellationToken = default)
	{
		CrawlOptions crawlOptions = invocation.Crawl;

		// FTP servers get 6 connections, more is often refused
		if (url.StartsWith(Constants.UriScheme.Ftp, StringComparison.Ordinal))
		{
			crawlOptions = crawlOptions with { Threads = 6 };
		}

		Credentials credentials = CliDefinition.WithUrlCredentials(invocation.Credentials, url);
		invocation = invocation with { Crawl = crawlOptions, Credentials = credentials };

		ScanSession session = new(new WebDirectory(null) { Name = Constants.Root, Url = url }, crawlOptions.Threads);

		OutputPaths outputPaths = new(session, invocation.Output);
		string databasePath = !string.IsNullOrWhiteSpace(invocation.Storage.DbPath) ? invocation.Storage.DbPath : outputPaths.Get("sqlite");
		bool resuming = invocation.Storage.Resume && File.Exists(databasePath);

		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

		builder.Services.AddSerilog(logger ?? new LoggerConfiguration().CreateLogger(), dispose: false);
		builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
		builder.Services.AddSingleton<IHostLifetime, Microsoft.Extensions.Hosting.Internal.ConsoleLifetime>();

		ILoggerFactory loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(logger);

		if (invocation.Storage.Resume && !resuming)
		{
			loggerFactory.CreateLogger(typeof(ScanApp)).LogWarning("--resume was specified but no existing database was found at '{path}'; starting a fresh scan.", databasePath);
		}

		ScanDatabase scanDatabase = resuming ?
			await ScanDatabase.OpenForResumeAsync(databasePath, cancellationToken, loggerFactory.CreateLogger<ScanDatabase>()) :
			await ScanDatabase.CreateAsync(databasePath, cancellationToken, loggerFactory.CreateLogger<ScanDatabase>());

		loggerFactory.CreateLogger(typeof(ScanApp)).LogInformation(resuming ? "Resuming scan from database: {path}" : "Scan database: {path}", databasePath);

		AddScanServices(builder.Services, invocation, session, scanDatabase, new ScanDatabaseFile(databasePath, resuming), outputPaths, interactiveConsole);

		return builder.Build();
	}

	private static void AddScanServices(IServiceCollection services, CliInvocation invocation, ScanSession session, ScanDatabase scanDatabase, ScanDatabaseFile databaseFile, OutputPaths outputPaths, bool interactiveConsole)
	{
		// What the command line said
		services.AddSingleton(invocation);
		services.AddSingleton(invocation.Crawl);
		services.AddSingleton(invocation.Http);
		services.AddSingleton(invocation.Output);
		services.AddSingleton(invocation.Storage);
		services.AddSingleton(invocation.Credentials);

		// The scan
		services.AddSingleton(session);
		services.AddSingleton(scanDatabase);
		services.AddSingleton(databaseFile);
		services.AddSingleton(outputPaths);
		services.AddSingleton<ScanController>();
		services.AddSingleton<IUserConsole>(new SystemUserConsole(interactiveConsole && !invocation.Output.Quit));
		services.AddSingleton<ConsoleTitle>();
		services.AddSingleton<IHistoryLog, HistoryLog>();

		// Getting directories
		services.AddSingleton<ScanHttp>();
		services.AddSingleton<BrowserService>();
		services.AddSingleton<FileSizeLookup>();
		services.AddSingleton<HttpDirectoryFetcher>();
		services.AddSingleton<FtpDirectoryFetcher>();
		services.AddSingleton<GoogleDriveFetcher>();
		services.AddSingleton<FailureClassifier>();
		services.AddSingleton<CrawlEngine>();
		services.AddSingleton(provider =>
		{
			ScanHttp scanHttp = provider.GetRequiredService<ScanHttp>();

			return new ParserServices
			{
				Session = session,
				Http = scanHttp.Client,
				Cookies = scanHttp.Cookies,
				Logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("OpenDirectoryDownloader.Parsing"),
				Console = provider.GetRequiredService<IUserConsole>(),
				Browser = invocation.Crawl.NoBrowser ? null : provider.GetRequiredService<BrowserService>(),
				Options = new ParserOptions
				{
					NoBrowser = invocation.Crawl.NoBrowser,
					GitHubToken = invocation.Credentials.GitHubToken
				}
			};
		});

		// After the scan
		services.AddSingleton<SessionJsonFactory>();
		services.AddSingleton<RedditStatsOutput>();
		services.AddSingleton<UrlListOutput>();
		services.AddSingleton<UrlUploadOutput>();
		services.AddSingleton<Aria2Output>();
		services.AddSingleton<SpeedtestOutput>();
		services.AddSingleton<StatisticsLogOutput>();
		services.AddSingleton<SessionJsonOutput>();
		services.AddSingleton<FinishedOutput>();
		services.AddSingleton<ClipboardOutput>();
		services.AddSingleton<ScanOutputPipeline>();

		// What runs
		services.AddHostedService<FlareSolverrLogStreamer>();
		services.AddHostedService<ScanWorker>();

		if (interactiveConsole)
		{
			services.AddHostedService<ConsoleCommandService>();
		}
	}
}
