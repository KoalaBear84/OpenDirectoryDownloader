using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Hosting;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Outputs.FileUpload;
using OpenDirectoryDownloader.Protocols.Ftp;
using OpenDirectoryDownloader.Protocols.Http;
using OpenDirectoryDownloader.Reporting;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using OpenDirectoryDownloader.Helpers;
using System.Text.Json;

namespace OpenDirectoryDownloader.Outputs;

/// <summary>What the output steps share: the finished scan, its report, and what earlier steps produced.</summary>
public sealed class ScanOutputContext
{
	public required ScanSession Session { get; init; }

	public required ScanDatabase Database { get; init; }

	public required ScanReport Report { get; init; }

	/// <summary>Finished directories below the root</summary>
	public required long TotalDirectories { get; init; }

	/// <summary>A --resume that found nothing left to do</summary>
	public bool ResumeNothingToDo { get; init; }

	/// <summary>An earlier run's speedtest is reused</summary>
	public bool SpeedtestRestored { get; init; }

	/// <summary>Set by the URL list step</summary>
	public string UrlListPath { get; set; }

	public bool ClipboardSucceeded { get; set; }

	/// <summary>Sites where every file has its own generated URL: a list of URLs means nothing there</summary>
	public bool IsGenericWebsite => Session.Root.Uri.Host is Constants.GoogleDriveDomain or Constants.BlitzfilesTechDomain or Constants.DropboxDomain or Constants.GoFileIoDomain or Constants.MediafireDomain or Constants.PixeldrainDomain;
}

/// <summary>One thing made after a finished scan.</summary>
public interface IScanOutput
{
	string Name { get; }

	Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Everything made after a finished scan, IN THIS ORDER (the URL list is needed before it can be uploaded, the
/// upload's link goes into the statistics). A step that fails is logged and the next one still runs.
/// </summary>
public sealed class ScanOutputPipeline(
	RedditStatsOutput redditStats,
	UrlListOutput urlList,
	UrlUploadOutput urlUpload,
	Aria2Output aria2,
	SpeedtestOutput speedtest,
	StatisticsLogOutput statisticsLog,
	SessionJsonOutput sessionJson,
	FinishedOutput finished,
	ClipboardOutput clipboard,
	ILogger<ScanOutputPipeline> logger)
{
	public IReadOnlyList<IScanOutput> Steps { get; } = [redditStats, urlList, urlUpload, aria2, speedtest, statisticsLog, sessionJson, finished, clipboard];

	public async Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		foreach (IScanOutput step in Steps)
		{
			try
			{
				await step.RunAsync(context, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error in output step {step}", step.Name);
			}
		}
	}
}

/// <summary>The Reddit markdown table, on the console</summary>
public sealed class RedditStatsOutput : IScanOutput
{
	public string Name => "Reddit statistics";

	public Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		Console.WriteLine(Statistics.GetSessionStats(context.Session, context.Report, onlyRedditStats: true, includeExtensions: true, totalDirectories: context.TotalDirectories));

		return Task.CompletedTask;
	}
}

/// <summary>The URLs of all files, naturally sorted, without duplicates (unless -f / --no-urls)</summary>
public sealed class UrlListOutput(OutputOptions outputOptions, OutputPaths outputPaths, ILogger<UrlListOutput> logger) : IScanOutput
{
	public string Name => "URL list";

	public async Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		if (outputOptions.NoUrls || context.IsGenericWebsite)
		{
			return;
		}

		if (context.Session.TotalFiles == 0)
		{
			logger.LogInformation("No URLs to save");
			Console.WriteLine("No URLs to save");
			return;
		}

		string urlsPath = outputPaths.Get("txt");

		// Never overwrite: the file may be from an earlier scan, and it isn't ours to replace (so it isn't uploaded either)
		if (File.Exists(urlsPath))
		{
			logger.LogInformation("URL list file already exists, not overwriting: {path}", urlsPath);
			Console.WriteLine($"URL list file already exists, not overwriting: {urlsPath}");
			return;
		}

		logger.LogInformation("Saving URL list to file..");
		Console.WriteLine("Saving URL list to file..");

		long distinctUrls = await WriteAsync(context.Database, urlsPath, cancellationToken);

		if (context.Session.TotalFiles != distinctUrls)
		{
			logger.LogWarning("Indexed files and unique files is not the same, please check results. Found a total of {totalUrls} files resulting in {distinctUrls} urls", context.Session.TotalFiles, distinctUrls);
		}

		context.UrlListPath = urlsPath;

		logger.LogInformation("Saved URL list to file: {path}", urlsPath);
		Console.WriteLine($"Saved URL list to file: {urlsPath}");
	}

	/// <summary>Writes the sorted, distinct URLs ('#' escaped, normalized where possible) to <paramref name="path"/>, streamed from the database.</summary>
	/// <returns>How many URLs were written</returns>
	public static async Task<long> WriteAsync(ScanDatabase database, string path, CancellationToken cancellationToken)
	{
		long count = 0;

		await using StreamWriter streamWriter = new(path, append: false, System.Text.Encoding.UTF8, bufferSize: 1 << 20);

		await foreach (string url in new ExternalUrlSorter(temporaryDirectory: Path.GetDirectoryName(path)).SortDistinctAsync(database.ReadFileUrlsAsync(cancellationToken), cancellationToken))
		{
			string safeUrl = url.Contains('#') ? url.Replace("#", "%23") : url;

			await streamWriter.WriteLineAsync(Uri.TryCreate(safeUrl, UriKind.Absolute, out Uri uri) ? uri.AbsoluteUri : safeUrl);
			count++;
		}

		return count;
	}
}

/// <summary>Uploads the URL list and remembers the link (-l / --upload-urls)</summary>
public sealed class UrlUploadOutput(OutputOptions outputOptions, ScanHttp scanHttp, IHistoryLog historyLog, ILogger<UrlUploadOutput> logger) : IScanOutput
{
	public string Name => "URL list upload";

	public async Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		if (!outputOptions.UploadUrls || context.UrlListPath is null)
		{
			return;
		}

		ScanSession session = context.Session;

		if (context.ResumeNothingToDo && !string.IsNullOrWhiteSpace(session.UploadedUrlsUrl))
		{
			Console.WriteLine($"URLs already uploaded, not uploading again. Uploaded URLs link: {session.UploadedUrlsUrl}");
			return;
		}

		// In this order, the next one when one fails
		IFileUploadSite[] uploadSites = [new Catbox(), new QuAx(), new ZeroXZero(), new GoFileIo(), new UploadFilesIo()];

		long fileSize = new FileInfo(context.UrlListPath).Length;

		foreach (IFileUploadSite uploadSite in uploadSites)
		{
			if (uploadSite.MaxFileSize is long maxFileSize && fileSize > maxFileSize)
			{
				logger.LogWarning("Skipping {siteName}: file is {fileSize}, the maximum is {maxSize}", uploadSite.Name, FileSizeHelper.ToHumanReadable(fileSize), FileSizeHelper.ToHumanReadable(maxFileSize));
				continue;
			}

			try
			{
				Console.WriteLine($"Uploading URLs ({FileSizeHelper.ToHumanReadable(new FileInfo(context.UrlListPath).Length)}) with {uploadSite.Name}..");

				IFileUploadSiteFile fileUploaderFile = await uploadSite.UploadFile(scanHttp.Client, context.UrlListPath, session, logger);
				historyLog.Write($"{uploadSite.Name} URL: {JsonSerializer.Serialize(fileUploaderFile, fileUploaderFile.GetType(), FileUploadJsonContext.Default)}");
				historyLog.Write($"{uploadSite.Name} full response: {session.UploadedUrlsResponse}");
				session.UploadedUrlsUrl = fileUploaderFile.Url;
				context.Database.MirrorUploadedUrlsUrl(session.UploadedUrlsUrl);
				Console.WriteLine($"Uploaded URLs link: {session.UploadedUrlsUrl}");
				break;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogWarning("Error uploading URLs: {error}", ex.Message);
			}
		}

		if (string.IsNullOrWhiteSpace(session.UploadedUrlsUrl))
		{
			session.UploadFailed = true;
			logger.LogError("The URLs file could not be uploaded to any site");
		}
	}
}

/// <summary>An aria2 input file: every URL with the directory to save it in (-i / --aria2-urls)</summary>
public sealed class Aria2Output(OutputOptions outputOptions, OutputPaths outputPaths, ILogger<Aria2Output> logger) : IScanOutput
{
	public string Name => "aria2 URL list";

	public async Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		if (!outputOptions.Aria2UrlsFile || context.IsGenericWebsite)
		{
			return;
		}

		if (context.Session.TotalFiles == 0)
		{
			logger.LogInformation("No URLs to save");
			Console.WriteLine("No URLs to save");
			return;
		}

		logger.LogInformation("Saving aria2 URL list to file..");
		Console.WriteLine("Saving aria2 URL list to file..");

		string urlsPath = outputPaths.Get("txt");
		string rootDir = string.IsNullOrWhiteSpace(outputOptions.Aria2RootDir) ? string.Empty : outputOptions.Aria2RootDir.TrimStart('/').TrimEnd('/');
		string urlDir = outputOptions.Aria2UrlDir ? Path.GetFileNameWithoutExtension(urlsPath) : string.Empty;

		string aria2Path = Path.Combine(Path.GetDirectoryName(urlsPath), $"{Path.GetFileNameWithoutExtension(urlsPath)}-aria2.txt");

		await using (StreamWriter streamWriter = new(aria2Path, append: false, System.Text.Encoding.UTF8, bufferSize: 1 << 20))
		{
			await context.Database.WalkTreeAsync(context.Session.Root.Url, new Aria2Writer(streamWriter, context.Session.Root.Url, rootDir, urlDir), cancellationToken);
		}

		logger.LogInformation("Saved aria2 URL list to file: {path}", aria2Path);
		Console.WriteLine($"Saved aria2 URL list to file: {aria2Path}");
	}

	private sealed class Aria2Writer(StreamWriter streamWriter, string rootUrl, string rootDir, string urlDir) : ScanDatabase.ITreeVisitor
	{
		public Task EnterDirectoryAsync(ScanDatabase.MirroredDirectory directory) => Task.CompletedTask;

		public Task LeaveDirectoryAsync(ScanDatabase.MirroredDirectory directory) => Task.CompletedTask;

		public async Task FileAsync(ScanDatabase.MirroredDirectory directory, ScanDatabase.MirroredFile file)
		{
			string fileUrl = file.Url;
			string fileName = file.FileName;

			string safeUrl = fileUrl.Contains('#') ? fileUrl.Replace("#", "%23") : fileUrl;
			await streamWriter.WriteLineAsync(Uri.TryCreate(safeUrl, UriKind.Absolute, out Uri uri) ? uri.AbsoluteUri : safeUrl);

			string directoryPath = (fileName.Length <= fileUrl.Length ? fileUrl[..^fileName.Length] : string.Empty).Replace(rootUrl, string.Empty).TrimEnd('/');

			if (!string.IsNullOrWhiteSpace(urlDir))
			{
				directoryPath = Path.Combine(urlDir, directoryPath.TrimStart('/').TrimEnd('/'));
			}

			if (!string.IsNullOrWhiteSpace(rootDir))
			{
				directoryPath = Path.Combine(rootDir, directoryPath.TrimStart('/').TrimEnd('/'));
			}

			await streamWriter.WriteLineAsync($"  dir={directoryPath}");
			await streamWriter.WriteLineAsync($"  out={fileName}");
		}
	}
}

/// <summary>Measures the download speed on the biggest file (-s / --speedtest)</summary>
public sealed class SpeedtestOutput(OutputOptions outputOptions, ScanHttp scanHttp, FtpDirectoryFetcher ftpFetcher, ILogger<SpeedtestOutput> logger) : IScanOutput
{
	public string Name => "Speedtest";

	public async Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		ScanSession session = context.Session;
		Uri rootUri = session.Root.Uri;
		bool isFtp = rootUri.Scheme is Constants.UriScheme.Ftp or Constants.UriScheme.Ftps;

		try
		{
			if (outputOptions.Speedtest &&
				!context.SpeedtestRestored &&
				session.TotalFiles > 0 &&
				rootUri.Host != Constants.GoogleDriveDomain &&
				!rootUri.Host.EndsWith(Constants.AmazonS3Domain) &&
				rootUri.Host is not (Constants.BlitzfilesTechDomain or Constants.DropboxDomain or Constants.GoFileIoDomain or Constants.GitHubDomain or Constants.MediafireDomain or Constants.PixeldrainDomain))
			{
				try
				{
					if (rootUri.Scheme is Constants.UriScheme.Http or Constants.UriScheme.Https)
					{
						ScanDatabase.MirroredFile biggestFile = await context.Database.GetLargestFileAsync() ?? throw new InvalidOperationException("No files found");

						Console.WriteLine("Starting speedtest (10-25 seconds)..");
						Console.WriteLine($"Test file: {FileSizeHelper.ToHumanReadable(biggestFile.FileSize)} {biggestFile.Url}");
						session.SpeedtestResult = await Speedtest.HttpAsync(scanHttp.Client, biggestFile.Url, logger, cancellationToken: cancellationToken);
					}
					else if (isFtp)
					{
						FluentFTP.AsyncFtpClient ftpClient = ftpFetcher.AnyConnectedClient;

						await ftpFetcher.CloseAllAsync(except: ftpClient);

						if (ftpClient is null)
						{
							Console.WriteLine("Cannot do speedtest because there is no connected FTP client anymore");
						}
						else
						{
							ScanDatabase.MirroredFile biggestFile = await context.Database.GetLargestFileAsync() ?? throw new InvalidOperationException("No files found");

							Console.WriteLine("Starting speedtest (10-25 seconds)..");
							Console.WriteLine($"Test file: {FileSizeHelper.ToHumanReadable(biggestFile.FileSize)} {biggestFile.Url}");
							session.SpeedtestResult = await Speedtest.FtpAsync(ftpClient, biggestFile.Url, logger, cancellationToken: cancellationToken);
						}
					}

					if (session.SpeedtestResult is { } result)
					{
						Console.WriteLine($"Finished speedtest. Downloaded: {FileSizeHelper.ToHumanReadable(result.DownloadedBytes)}, Time: {result.ElapsedMilliseconds / 1000d:F1} s, Speed: {result.MaxMBsPerSecond:F1} MB/s ({result.MaxMBsPerSecond * 8:F0} mbit)");
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// An empty result is reported as Failed
					session.SpeedtestResult = new SpeedtestResult();
					logger.LogError(ex, "Speedtest failed");
				}
			}
			else if (context.SpeedtestRestored)
			{
				logger.LogInformation("Speedtest skipped because a result is already stored in the database");
			}
			else
			{
				logger.LogInformation("Speedtest skipped because of general service or disabled through command line");
			}

			if (session.SpeedtestResult != null && !context.SpeedtestRestored)
			{
				context.Database.MirrorSpeedtestResult(session.SpeedtestResult);
			}
		}
		finally
		{
			if (isFtp)
			{
				await ftpFetcher.CloseAllAsync();
			}
		}
	}
}

/// <summary>The full statistics to the log and the history log, and on the console (unless -r), then the URLs with errors</summary>
public sealed class StatisticsLogOutput(OutputOptions outputOptions, IHistoryLog historyLog, ILogger<StatisticsLogOutput> logger) : IScanOutput
{
	public string Name => "Statistics";

	public Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		ScanSession session = context.Session;

		logger.LogInformation("Logging sessions stats..");

		string sessionStats = Statistics.GetSessionStats(session, context.Report, includeExtensions: true, includeBanner: true, totalDirectories: context.TotalDirectories);
		logger.LogInformation("{statistics}", sessionStats);
		historyLog.Write(sessionStats);
		logger.LogInformation("Logged sessions stats");

		if (!outputOptions.NoReddit)
		{
			// Also on screen: when saving the links or JSON fails and the logs keep filling, this is still there
			Console.WriteLine(sessionStats);
		}

		if (session.UrlsWithErrors.Count != 0)
		{
			logger.LogInformation("URLs with errors ({count}):", session.UrlsWithErrors.Count);
			Console.WriteLine($"URLs with errors ({session.UrlsWithErrors.Count}):");

			foreach (string urlWithError in session.UrlsWithErrors.OrderBy(u => u, NaturalSortStringComparer.InvariantCulture))
			{
				logger.LogInformation("{url}", urlWithError);
				Console.WriteLine(urlWithError);
			}
		}

		return Task.CompletedTask;
	}
}

/// <summary>The session as JSON (-j / --json)</summary>
public sealed class SessionJsonOutput(OutputOptions outputOptions, SessionJsonFactory sessionJsonFactory) : IScanOutput
{
	public string Name => "JSON";

	public async Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		if (outputOptions.Json)
		{
			await sessionJsonFactory.SaveAsync(context.TotalDirectories, cancellationToken);
		}
	}
}

/// <summary>The end of the scan, on the console and in the title</summary>
public sealed class FinishedOutput(ConsoleTitle consoleTitle, ILogger<FinishedOutput> logger) : IScanOutput
{
	public string Name => "Finished";

	public Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		logger.LogInformation("Finished indexing!");
		Console.WriteLine("Finished indexing!");

		consoleTitle.Set($"✔ {context.Session.Root.Uri.Host.Replace("www.", string.Empty)}");

		return Task.CompletedTask;
	}
}

/// <summary>The Reddit statistics to the clipboard (-c / --clipboard)</summary>
public sealed class ClipboardOutput(OutputOptions outputOptions, ILogger<ClipboardOutput> logger) : IScanOutput
{
	public string Name => "Clipboard";

	public Task RunAsync(ScanOutputContext context, CancellationToken cancellationToken)
	{
		if (!outputOptions.Clipboard)
		{
			return Task.CompletedTask;
		}

		if (ClipboardWriter.TrySetText(Statistics.GetSessionStats(context.Session, context.Report, includeExtensions: true, onlyRedditStats: true, totalDirectories: context.TotalDirectories)))
		{
			Console.WriteLine("Copied Reddit stats to clipboard!");
			context.ClipboardSucceeded = true;
		}
		else
		{
			logger.LogError("Error copying stats to clipboard");
		}

		return Task.CompletedTask;
	}
}
