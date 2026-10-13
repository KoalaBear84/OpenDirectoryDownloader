using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Crawling;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Outputs;
using OpenDirectoryDownloader.Protocols.Ftp;
using OpenDirectoryDownloader.Protocols.Http;
using OpenDirectoryDownloader.Reporting;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using OpenDirectoryDownloader.Threading;
using OpenDirectoryDownloader.Urls;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Hosting;

/// <summary>Where the scan database is, and whether it continues an earlier scan.</summary>
public sealed record ScanDatabaseFile(string Path, bool Resuming);

/// <summary>
/// Runs one scan: prepares it (resume, FTP server info, site limits), crawls, makes the outputs, and closes the
/// scan database. Then stops the application with --quit, or waits for the user.
/// </summary>
public sealed partial class ScanWorker(
	ScanSession session,
	ScanDatabase scanDatabase,
	ScanDatabaseFile databaseFile,
	CliInvocation invocation,
	CrawlEngine crawlEngine,
	FtpDirectoryFetcher ftpFetcher,
	ScanHttp scanHttp,
	ScanOutputPipeline outputPipeline,
	ScanController controller,
	IUserConsole userConsole,
	IHostApplicationLifetime lifetime,
	ILogger<ScanWorker> logger) : BackgroundService
{
	private static readonly TimeSpan StatisticsInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan FileSizeStatisticsInterval = TimeSpan.FromSeconds(5);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Paused (P key) or stopped (ESC, Ctrl+C): both end the crawl
		using CancellationTokenSource crawlCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, controller.PauseToken);
		using CancellationTokenSource statisticsCancellation = new();

		bool resumeNothingToDo = false;
		bool speedtestRestored = false;
		bool completed = false;
		Task statistics = Task.CompletedTask;

		try
		{
			session.Started = DateTimeOffset.UtcNow;

			List<WebDirectory> directories = [session.Root];
			List<WebFile> filesForSize = [];

			if (databaseFile.Resuming)
			{
				ScanResume.Result resumeResult = await ResumeAsync();

				if (resumeResult is not null)
				{
					directories = resumeResult.DirectoriesToRequeue;
					filesForSize = resumeResult.FilesToRequeueForSize;
					resumeNothingToDo = directories.Count == 0 && filesForSize.Count == 0;
					speedtestRestored = session.SpeedtestResult is not null;
				}
			}

			// Nothing left to do: don't add an empty attempt to the database's history
			int runNumber = resumeNothingToDo ? 0 : await scanDatabase.BeginRunAsync(session, session.Root.Url, isFreshScan: !databaseFile.Resuming);

			if (databaseFile.Resuming)
			{
				await PrintResumeSummaryAsync(runNumber, directories.Count, filesForSize.Count);
			}

			await PrepareSiteAsync(crawlCancellation.Token);

			statistics = ShowStatisticsAsync(statisticsCancellation.Token);

			await crawlEngine.RunAsync(directories, filesForSize, crawlCancellation.Token);

			await statisticsCancellation.CancelAsync();
			await statistics;

			// Finished: the in-memory tree is complete (closed subtrees keep their totals)
			session.Finished = DateTimeOffset.UtcNow;
			session.TotalFiles = session.Root.TotalFiles;
			session.TotalFileSizeEstimated = session.Root.TotalFileSize;
			long totalDirectories = session.Root.TotalDirectories;

			// The running counters were for the display, the final report uses the recount
			session.Counters.Restore(runningFiles: session.TotalFiles, runningFileSize: session.TotalFileSizeEstimated, directoriesFinished: totalDirectories);

			if (!resumeNothingToDo)
			{
				scanDatabase.MarkCompleted(session.Finished);
			}

			ScanOutputContext outputContext = new()
			{
				Session = session,
				Database = scanDatabase,
				Report = controller.FinalReport = await ScanReport.TryCreateAsync(scanDatabase, logger),
				TotalDirectories = totalDirectories,
				ResumeNothingToDo = resumeNothingToDo,
				SpeedtestRestored = speedtestRestored
			};

			await outputPipeline.RunAsync(outputContext, stoppingToken);

			controller.MarkFinished();
			completed = true;

			if (!invocation.Output.Quit && userConsole.IsInteractive)
			{
				Console.WriteLine(outputContext.ClipboardSucceeded ? "Press ESC to exit!" : "Press ESC to exit! Or C to copy to clipboard and quit!");
			}
		}
		catch (OperationCanceledException) when (crawlCancellation.IsCancellationRequested)
		{
			// Paused or stopped: not an error
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error in indexing task");
		}
		finally
		{
			await statisticsCancellation.CancelAsync();
			await statistics;

			await CloseDatabaseAsync();

			if (controller.Paused)
			{
				logger.LogInformation("Paused.");
			}

			controller.MarkCompleted();

			if (invocation.Output.Quit && (completed || !controller.Paused))
			{
				lifetime.StopApplication();
			}
		}
	}

	/// <summary>Rebuilds the tree from the database and restores the counters of the earlier runs.</summary>
	private async Task<ScanResume.Result> ResumeAsync()
	{
		bool skipFileSizeLookups = session.Root.Uri.Scheme is Constants.UriScheme.Ftp or Constants.UriScheme.Ftps;

		ScanResume.Result resumeResult = await ScanResume.BuildAsync(scanDatabase, session.Root.Url, skipFileSizeLookups, await ShouldRetryErrorsAsync());

		if (resumeResult is null)
		{
			logger.LogWarning("Database at '{path}' has no record of '{url}'; starting a fresh scan instead of resuming.", databaseFile.Path, session.Root.Url);
			return null;
		}

		session.Root = resumeResult.Root;
		session.ProcessedUrls = resumeResult.ProcessedUrls;

		if (await scanDatabase.GetLatestRunStatsAsync() is ScanDatabase.SessionStatsSnapshot previousStats)
		{
			// So the totals are those of the whole scan, not just this part
			session.Counters.Restore(httpTraffic: previousStats.TotalHttpTraffic, httpRequests: previousStats.TotalHttpRequests, errors: previousStats.Errors, skipped: previousStats.Skipped);

			foreach ((int statusCode, int count) in previousStats.HttpStatusCodes)
			{
				session.HttpStatusCodes.Add(statusCode, count);
			}
		}

		if (await scanDatabase.GetScanInfoAsync() is ScanDatabase.ScanInfoSnapshot scanInfo)
		{
			session.Started = scanInfo.FirstStartedAtUtc;

			if (!string.Equals(scanInfo.RootUrl, session.Root.Url, StringComparison.Ordinal))
			{
				logger.LogWarning("This database was originally for '{originalUrl}', not '{url}' - continuing anyway, but this looks like the wrong database/URL combination.", scanInfo.RootUrl, session.Root.Url);
			}
		}

		if (resumeResult.DirectoriesToRequeue.Count == 0 && resumeResult.FilesToRequeueForSize.Count == 0)
		{
			// The URL list can't have changed: show the earlier upload instead of uploading it again
			session.UploadedUrlsUrl = await scanDatabase.GetUploadedUrlsUrlAsync();
		}

		if (await scanDatabase.GetLatestSpeedtestResultAsync() is { DownloadedBytes: > 0 } previousSpeedtest)
		{
			// An earlier run's speedtest is reused instead of measured (and stored) again on every resume
			session.SpeedtestResult = new SpeedtestResult
			{
				DownloadedBytes = previousSpeedtest.DownloadedBytes,
				ElapsedMilliseconds = previousSpeedtest.ElapsedMilliseconds,
				MaxBytesPerSecond = previousSpeedtest.MaxBytesPerSecond,
				MaxKBsPerSecond = previousSpeedtest.MaxBytesPerSecond / 1024d,
				MaxMBsPerSecond = previousSpeedtest.MaxBytesPerSecond / 1024d / 1024d
			};
		}

		if (!resumeResult.DirectoriesToRequeue.Contains(resumeResult.Root))
		{
			// The root is done: the first request of this run is not the root's, it gets no root fallbacks
			scanHttp.State.FirstRequest = false;
		}

		return resumeResult;
	}

	/// <summary>
	/// Whether a resume retries the directories that had errors: always with --retry-errors, never without a person
	/// to ask (or with --quit), else asked.
	/// </summary>
	private async Task<bool> ShouldRetryErrorsAsync()
	{
		if (invocation.Storage.RetryErrors)
		{
			return true;
		}

		long erroredCount = await scanDatabase.CountErroredDirectoriesAsync();

		if (erroredCount == 0)
		{
			return false;
		}

		if (!userConsole.IsInteractive || invocation.Output.Quit)
		{
			logger.LogWarning("{count} director{suffix} had errors during the previous run; not retrying them (non-interactive session - pass --retry-errors to retry automatically).", erroredCount, erroredCount == 1 ? "y" : "ies");
			return false;
		}

		string answer = userConsole.Ask($"Found {UrlHelper.FormatWithThousands(erroredCount)} director{(erroredCount == 1 ? "y" : "ies")} that had errors during the previous run. Retry {(erroredCount == 1 ? "it" : "them")}? [y/N]: ");

		return answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
	}

	private async Task PrintResumeSummaryAsync(int runNumber, int directoriesToRequeue, int filesToRequeue)
	{
		long totalDirectoriesInDatabase = await scanDatabase.CountDirectoriesAsync();
		long totalFilesInDatabase = await scanDatabase.CountFilesAsync();
		long priorRunCount = await scanDatabase.CountRunsAsync() - (runNumber == 0 ? 0 : 1);
		ScanDatabase.ScanInfoSnapshot scanInfo = await scanDatabase.GetScanInfoAsync();

		string sitePrefix = scanInfo is null ? string.Empty :
			$"""

			  Site: {scanInfo.RootUrl}
			  {(runNumber == 0 ? $"Nothing left to do, so no new attempt was recorded ({UrlHelper.FormatWithThousands(priorRunCount)} prior attempt{(priorRunCount == 1 ? "" : "s")})" : $"This is attempt #{UrlHelper.FormatWithThousands(runNumber)} ({UrlHelper.FormatWithThousands(priorRunCount)} prior attempt{(priorRunCount == 1 ? "" : "s")})")}; first started {scanInfo.FirstStartedAtUtc:yyyy-MM-dd HH:mm} UTC.
			""";

		string summary =
			$"""
			Successfully resumed from database: {databaseFile.Path}{sitePrefix}
			  Already in database: {UrlHelper.FormatWithThousands(session.ProcessedUrls.Count)} of {UrlHelper.FormatWithThousands(totalDirectoriesInDatabase)} directories finished, {UrlHelper.FormatWithThousands(totalFilesInDatabase - filesToRequeue)} of {UrlHelper.FormatWithThousands(totalFilesInDatabase)} files with a known size.
			  Queued: {UrlHelper.FormatWithThousands(directoriesToRequeue)} director{(directoriesToRequeue == 1 ? "y" : "ies")} to (re)process, {UrlHelper.FormatWithThousands(filesToRequeue)} file size{(filesToRequeue == 1 ? "" : "s")} to look up.
			""";

		Console.WriteLine(summary);
		logger.LogInformation("{summary}", summary);
	}

	/// <summary>What some sites need before the crawl: lower limits, the FTP server's info.</summary>
	private async Task PrepareSiteAsync(CancellationToken cancellationToken)
	{
		Uri rootUri = session.Root.Uri;

		if (rootUri.Host == Constants.GoogleDriveDomain)
		{
			logger.LogWarning("{indexer} scanning is limited to {directoriesPerSecond} directories per second!", "Google Drive", 9);
		}

		if (rootUri.Host == Constants.GitHubDomain)
		{
			logger.LogWarning("{indexer} scanning has a very low rate limiting of {requestsPerHour} directories/requests per hour!", "GitHub", 60);

			if (session.Concurrency.Effective != 1)
			{
				logger.LogWarning("Reduce threads to {threadCount} because of {indexer}", 1, "GitHub");
			}

			session.Concurrency.SetCap(ConcurrencyReasons.GitHub, 1);
		}

		if (rootUri.Scheme is not (Constants.UriScheme.Ftp or Constants.UriScheme.Ftps))
		{
			return;
		}

		logger.LogWarning("Retrieving FTP(S) software!");

		if (rootUri.Scheme == Constants.UriScheme.Ftps && rootUri.Port == -1)
		{
			logger.LogWarning("Using default port (990) for FTPS");

			session.Root.Url = new UriBuilder(rootUri) { Port = 990 }.Uri.ToString();
		}

		string serverInfo = await ftpFetcher.GetServerInfoAsync(session.Root, cancellationToken);

		if (string.IsNullOrWhiteSpace(serverInfo))
		{
			serverInfo = "Failed or no server info available.";
		}
		else
		{
			// No IP addresses in the report
			serverInfo = ConnectedToIpRegex().Replace(serverInfo, "$1IP Address");

			session.Description = $"FTP INFO{Environment.NewLine}{serverInfo}";
		}

		logger.LogWarning("{serverInfo}", serverInfo);
	}

	/// <summary>Every 30 seconds (5 while looking up file sizes): the counters into the database (for --resume), and the statistics on screen.</summary>
	private async Task ShowStatisticsAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(crawlEngine.CrawlingDirectories ? StatisticsInterval : FileSizeStatisticsInterval, cancellationToken);

				try
				{
					scanDatabase.MirrorSessionStats(session);

					if (session.StatisticsSuppressed || session.RateLimit.AllWaiting(crawlEngine.RunningDirectoryWorkers) || !crawlEngine.Running)
					{
						continue;
					}

					logger.LogWarning("{statistics}", $"{Statistics.GetSessionStats(session)}{Environment.NewLine}{QueueStatus(crawlEngine)}");
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Error showing statistics");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Done
		}
	}

	public static string QueueStatus(CrawlEngine crawlEngine) =>
		$"Queue: {UrlHelper.FormatWithThousands(crawlEngine.QueuedDirectories)} ({crawlEngine.RunningDirectoryWorkers} threads), Queue (filesizes): {UrlHelper.FormatWithThousands(crawlEngine.QueuedFiles)} ({crawlEngine.RunningFileWorkers} threads)";

	/// <summary>
	/// Saves the counters and closes the database. It is deleted after a finished scan, unless it should be kept:
	/// --keep-db, --resume, and a paused scan (that is what it was paused for).
	/// </summary>
	private async Task CloseDatabaseAsync()
	{
		bool keep = invocation.Storage.KeepDb || invocation.Storage.Resume || controller.Paused;

		try
		{
			if (keep)
			{
				scanDatabase.MirrorSessionStats(session);
				await scanDatabase.DisposeAsync();
			}
			else
			{
				await scanDatabase.DiscardAsync();
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error closing scan database '{path}'", databaseFile.Path);
		}

		if (keep)
		{
			logger.LogInformation("Kept scan database: {path}", databaseFile.Path);
			return;
		}

		try
		{
			ScanDatabase.DeleteFiles(databaseFile.Path);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Could not delete scan database '{path}'", databaseFile.Path);
		}
	}

	[GeneratedRegex(@"(Connected to )(\d*\.\d*.\d*.\d*)")]
	private static partial Regex ConnectedToIpRegex();
}
