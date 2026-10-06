using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Listings;
using OpenDirectoryDownloader.Protocols;
using OpenDirectoryDownloader.Protocols.Ftp;
using OpenDirectoryDownloader.Protocols.Http;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace OpenDirectoryDownloader.Crawling;

/// <summary>
/// Crawls the tree: directory workers take directories from a channel, fetch and parse them, and queue the
/// subdirectories they find; file size workers look up the sizes listings don't show. Nothing polls: workers wait
/// on the channels, and the directory channel completes when no directory is queued or in progress anymore.
/// </summary>
/// <remarks>
/// Every finished subtree is dropped from memory (the scan database has it): a directory counts its outstanding
/// work (its own parse, its queued subdirectories, its files waiting for a size) and when that reaches zero it and
/// then its parents close, see <see cref="WebDirectory.TryClose"/>.
/// </remarks>
public sealed class CrawlEngine(
	ScanSession session,
	ScanDatabase scanDatabase,
	HttpDirectoryFetcher httpFetcher,
	FtpDirectoryFetcher ftpFetcher,
	GoogleDriveFetcher googleDriveFetcher,
	FileSizeLookup fileSizeLookup,
	FailureClassifier failureClassifier,
	CrawlOptions crawlOptions,
	ILogger<CrawlEngine> logger)
{
	/// <summary>Attempts per directory at most; the classifier normally gives up much earlier</summary>
	private const int MaxAttempts = 100;

	/// <summary>A directory, all retries included, may take this long (more when the server asks to wait)</summary>
	private static readonly TimeSpan DirectoryTimeLimit = TimeSpan.FromMinutes(5);

	private readonly Channel<WebDirectory> _directories = Channel.CreateUnbounded<WebDirectory>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
	private readonly Channel<WebFile> _files = Channel.CreateUnbounded<WebFile>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

	/// <summary>Directories queued or in progress; the channel completes when it reaches zero</summary>
	private long _outstandingDirectories;

	/// <summary>Files queued or in progress, plus one while directories are still being crawled (they can add files)</summary>
	private long _outstandingFiles = 1;

	private int _queuedDirectories;
	private int _queuedFiles;
	private int _runningDirectoryWorkers;
	private int _runningFileWorkers;

	private readonly ConcurrentDictionary<string, DirectoryWork> _inProgress = new();

	private bool IsFtp => session.Root.Uri.Scheme is Constants.UriScheme.Ftp or Constants.UriScheme.Ftps;

	private bool IsGoogleDrive => session.Root.Uri.Host == Constants.GoogleDriveDomain;

	public int QueuedDirectories => Volatile.Read(ref _queuedDirectories);

	public int QueuedFiles => Volatile.Read(ref _queuedFiles);

	public int RunningDirectoryWorkers => Volatile.Read(ref _runningDirectoryWorkers);

	public int RunningFileWorkers => Volatile.Read(ref _runningFileWorkers);

	/// <summary>Whether directories are still being crawled (as opposed to only file sizes being looked up)</summary>
	public bool CrawlingDirectories => !_directories.Reader.Completion.IsCompleted;

	public bool Running { get; private set; }

	/// <summary>What every worker is busy with, for the T key</summary>
	public IReadOnlyCollection<DirectoryWork> InProgress => [.. _inProgress.Values];

	/// <summary>Crawls from the given directories (the root, or what a resumed scan left) until everything is done or <paramref name="cancellationToken"/> stops it.</summary>
	public async Task RunAsync(IReadOnlyList<WebDirectory> directories, IReadOnlyList<WebFile> filesForSize, CancellationToken cancellationToken)
	{
		Running = true;

		try
		{
			foreach (WebDirectory directory in directories)
			{
				EnqueueDirectory(directory);
			}

			foreach (WebFile file in filesForSize)
			{
				EnqueueFile(file);
			}

			if (Interlocked.Read(ref _outstandingDirectories) == 0)
			{
				CompleteDirectories();
			}

			int threads = session.Concurrency.Configured;
			int width = threads.ToString().Length;

			Task[] directoryWorkers = [.. Enumerable.Range(1, threads).Select(i => Task.Run(() => DirectoryWorkerAsync($"P{i.ToString().PadLeft(width, '0')}", cancellationToken), CancellationToken.None))];
			Task[] fileWorkers = [.. Enumerable.Range(1, threads).Select(i => Task.Run(() => FileWorkerAsync($"P{i.ToString().PadLeft(width, '0')}", cancellationToken), CancellationToken.None))];

			await Task.WhenAll(directoryWorkers);

			logger.LogInformation("Finished indexing");

			if (QueuedFiles > 0)
			{
				logger.LogWarning("Retrieving filesize of {count} urls", QueuedFiles);
			}

			await Task.WhenAll(fileWorkers);
		}
		finally
		{
			Running = false;
		}
	}

	private void EnqueueDirectory(WebDirectory directory)
	{
		Interlocked.Increment(ref _outstandingDirectories);
		Interlocked.Increment(ref _queuedDirectories);

		if (!_directories.Writer.TryWrite(directory))
		{
			// Only after a pause (the channel is complete)
			Interlocked.Decrement(ref _queuedDirectories);
			Interlocked.Decrement(ref _outstandingDirectories);
		}
	}

	private void EnqueueFile(WebFile file)
	{
		Interlocked.Increment(ref _outstandingFiles);
		Interlocked.Increment(ref _queuedFiles);

		if (!_files.Writer.TryWrite(file))
		{
			Interlocked.Decrement(ref _queuedFiles);
			Interlocked.Decrement(ref _outstandingFiles);
		}
	}

	private void CompleteDirectories()
	{
		_directories.Writer.TryComplete();

		// Directories can't add files anymore
		if (Interlocked.Decrement(ref _outstandingFiles) == 0)
		{
			_files.Writer.TryComplete();
		}
	}

	private async Task DirectoryWorkerAsync(string workerName, CancellationToken cancellationToken)
	{
		logger.LogDebug("[{thread}] Start", workerName);

		await foreach (WebDirectory directory in _directories.Reader.ReadAllAsync(cancellationToken))
		{
			Interlocked.Decrement(ref _queuedDirectories);

			try
			{
				using (await session.Concurrency.AcquireAsync(cancellationToken))
				{
					Interlocked.Increment(ref _runningDirectoryWorkers);

					try
					{
						await ProcessDirectoryAsync(workerName, directory, cancellationToken);
					}
					finally
					{
						Interlocked.Decrement(ref _runningDirectoryWorkers);
					}
				}
			}
			finally
			{
				if (Interlocked.Decrement(ref _outstandingDirectories) == 0)
				{
					CompleteDirectories();
				}
			}

			if (crawlOptions.WaitSecondsBetweenCalls > 0)
			{
				await Task.Delay(TimeSpan.FromSeconds(crawlOptions.WaitSecondsBetweenCalls), cancellationToken);
			}
		}

		logger.LogDebug("[{thread}] Finished", workerName);
	}

	private async Task ProcessDirectoryAsync(string workerName, WebDirectory webDirectory, CancellationToken cancellationToken)
	{
		DirectoryWork work = new(webDirectory, workerName, ConvertToFile);
		bool requeued = false;

		_inProgress[workerName] = work;

		try
		{
			// A directory can be listed under two parents before either is processed
			if (!session.ProcessedUrls.Add(webDirectory.Url))
			{
				return;
			}

			logger.LogDebug("[{thread}] Begin processing {url}", workerName, webDirectory.Url);

			if (IsFtp)
			{
				(FtpDirectoryFetcher.Outcome outcome, WebDirectory parsedWebDirectory) = await ftpFetcher.FetchAsync(workerName, webDirectory, cancellationToken);

				switch (outcome)
				{
					case FtpDirectoryFetcher.Outcome.MaxConnections:
						if (webDirectory.ParentDirectory is null)
						{
							webDirectory.Error = true;
							throw new Exception("Error checking FTP because maximum connections reached");
						}

						// For another worker, this one's connection goes
						session.ProcessedUrls.Remove(webDirectory.Url);
						await ftpFetcher.DisconnectAsync(workerName);
						requeued = true;
						EnqueueDirectory(webDirectory);
						return;
					case FtpDirectoryFetcher.Outcome.Failed:
						throw new DirectoryAbandonedException(null);
				}

				ListingPostProcessor.CheckParsedResults(parsedWebDirectory, session.Root.Uri.ToString(), true, logger);
				await AddProcessedAsync(webDirectory, parsedWebDirectory, processSubdirectories: true, cancellationToken);
			}
			else if (IsGoogleDrive)
			{
				await AddProcessedAsync(webDirectory, await googleDriveFetcher.FetchAsync(webDirectory, cancellationToken), processSubdirectories: true, cancellationToken);
			}
			else if (session.Root.Uri.Host.EndsWith(Constants.AmazonS3Domain) ||
				session.Root.Uri.Host.EndsWith(Constants.GitHubDomain) ||
				session.Root.Uri.Host == Constants.BlitzfilesTechDomain ||
				session.Root.Uri.Host == Constants.DropboxDomain ||
				ListingPostProcessor.SameHostAndDirectoryFile(session.Root.Uri, webDirectory.Uri))
			{
				logger.LogDebug("[{thread}] Start download '{url}'", workerName, webDirectory.Url);
				session.Counters.AddHttpRequest();

				await FetchWithRetriesAsync(work, cancellationToken);
			}
			else
			{
				logger.LogWarning("[{thread}] Skipped result of '{url}' because it is not the same host or path", workerName, webDirectory.Url);
				session.Counters.AddSkipped();
			}

			logger.LogDebug("[{thread}] Finished processing {url}", workerName, webDirectory.Url);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Paused: not finished, a resume does it again
			throw;
		}
		catch (DirectoryAbandonedException)
		{
			session.ReportError(webDirectory.Url);
			webDirectory.Error = true;

			if (webDirectory.ParentDirectory?.Url != null)
			{
				logger.LogError("Skipped processing Url: '{url}' from parent '{parentUrl}'", webDirectory.Url, webDirectory.ParentDirectory.Url);
			}
			else
			{
				logger.LogError("Skipped processing Url: '{url}'", webDirectory.Url);
				session.Root.Error = true;
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error processing Url: '{url}' from parent '{parentUrl}'", webDirectory.Url, webDirectory.ParentDirectory?.Url);
		}
		finally
		{
			_inProgress.TryRemove(workerName, out _);

			if (!requeued && !cancellationToken.IsCancellationRequested)
			{
				if (!work.BecameFile)
				{
					webDirectory.Finished = true;
					session.Counters.AddDirectoryFinished();
					scanDatabase.MirrorDirectory(webDirectory);
				}

				// Its own part of the work is done
				WebDirectory.TryClose(webDirectory);
			}
		}
	}

	/// <summary>Fetches over HTTP, retrying as the <see cref="FailureClassifier"/> decides, within the directory's time limit.</summary>
	private async Task FetchWithRetriesAsync(DirectoryWork work, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeLimit.CancelAfter(DirectoryTimeLimit);
		work.ExtendTimeout = wait => timeLimit.CancelAfter(wait + DirectoryTimeLimit);

		for (int attempt = 1; ; attempt++)
		{
			try
			{
				await httpFetcher.FetchAsync(work, (parsedWebDirectory, processSubdirectories) => AddProcessedAsync(work.Directory, parsedWebDirectory, processSubdirectories, cancellationToken), timeLimit.Token);

				return;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				TimeSpan wait = TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, attempt))) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200));

				if (timeLimit.IsCancellationRequested || attempt >= MaxAttempts || !failureClassifier.ShouldRetry(ex, attempt, work.Directory, work.WorkerName, wait))
				{
					throw new DirectoryAbandonedException(ex);
				}

				try
				{
					await Task.Delay(wait, timeLimit.Token);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					throw new DirectoryAbandonedException(ex);
				}
			}
		}
	}

	/// <summary>
	/// Puts a parse result into the tree: copies it onto the directory, stores it, queues its new subdirectories
	/// and the files that need a size.
	/// </summary>
	/// <param name="processSubdirectories">False when the result is the whole tree below the directory (some sites list it all at once): then it is stored, not crawled</param>
	private async Task AddProcessedAsync(WebDirectory webDirectory, WebDirectory parsedWebDirectory, bool processSubdirectories, CancellationToken cancellationToken)
	{
		// Some site parsers fill the directory they were given instead of returning a new one
		bool sameDirectory = ReferenceEquals(webDirectory, parsedWebDirectory);

		// Against what was there before, in case a directory is processed again (--retry-errors)
		int previousFileCount = sameDirectory ? 0 : webDirectory.Files.Count;
		long previousFileSize = sameDirectory ? 0 : SumFileSizes(webDirectory.Files);

		if (!sameDirectory)
		{
			webDirectory.Description = parsedWebDirectory.Description;
			webDirectory.Files = parsedWebDirectory.Files;
			webDirectory.Finished = parsedWebDirectory.Finished;
			webDirectory.Name = parsedWebDirectory.Name;
			webDirectory.Subdirectories = parsedWebDirectory.Subdirectories;
			webDirectory.Url = parsedWebDirectory.Url;
			webDirectory.Error = parsedWebDirectory.Error;
			webDirectory.Parser = parsedWebDirectory.Parser;
		}

		webDirectory.ContentFingerprint = webDirectory.ComputeContentFingerprint();

		session.Counters.AddFiles(webDirectory.Files.Count - previousFileCount, SumFileSizes(webDirectory.Files) - previousFileSize);

		// The parse result's children point at the detached result, make them point at the tree
		foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
		{
			subdirectory.ParentDirectory = webDirectory;
		}

		foreach (WebFile file in webDirectory.Files)
		{
			file.AttachTo(webDirectory);
		}

		List<WebDirectory> newSubdirectories = [];

		if (processSubdirectories)
		{
			foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
			{
				if (session.ProcessedUrls.Contains(subdirectory.Url))
				{
					continue;
				}

				if (subdirectory.Uri.Host != Constants.AmazonS3Domain &&
					subdirectory.Uri.Host != Constants.BlitzfilesTechDomain &&
					subdirectory.Uri.Host != Constants.DropboxDomain &&
					subdirectory.Uri.Host != Constants.GoogleDriveDomain &&
					!ListingPostProcessor.SameHostAndDirectoryFile(session.Root.Uri, subdirectory.Uri))
				{
					logger.LogDebug("Removed subdirectory '{url}' from parsed webdirectory because it is not the same host", subdirectory.Uri);
					continue;
				}

				newSubdirectories.Add(subdirectory);
			}
		}

		// Stored before the subdirectories are queued: they need their parent's id
		await scanDatabase.MirrorDirectoryListingAsync(webDirectory, newSubdirectories, cancellationToken);

		if (!processSubdirectories)
		{
			await StoreCompleteSubtreeAsync(webDirectory, cancellationToken);
		}

		foreach (WebDirectory subdirectory in newSubdirectories)
		{
			// Before it is queued: a worker could otherwise finish it before its parent counts it
			Interlocked.Increment(ref webDirectory.PendingWork);
			EnqueueDirectory(subdirectory);
		}

		if (parsedWebDirectory.Error)
		{
			session.UrlsWithErrors.Add(webDirectory.Url);
		}

		if (!IsFtp)
		{
			foreach (WebFile webFile in webDirectory.Files)
			{
				if ((webFile.FileSize is null && !crawlOptions.FastScan) || crawlOptions.ExactFileSizes)
				{
					Interlocked.Increment(ref webDirectory.PendingWork);
					EnqueueFile(webFile);
				}
			}
		}
	}

	/// <summary>The site listed everything below this directory at once: all of it is final, store it.</summary>
	private async Task StoreCompleteSubtreeAsync(WebDirectory webDirectory, CancellationToken cancellationToken)
	{
		foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
		{
			subdirectory.ParentDirectory = webDirectory;
			subdirectory.Finished = true;

			foreach (WebFile file in subdirectory.Files)
			{
				file.AttachTo(subdirectory);
			}

			session.Counters.AddFiles(subdirectory.Files.Count, SumFileSizes(subdirectory.Files));

			await scanDatabase.MirrorDirectoryListingAsync(subdirectory, subdirectory.Subdirectories, cancellationToken);
			await StoreCompleteSubtreeAsync(subdirectory, cancellationToken);
		}
	}

	/// <summary>The directory turned out to be a file: its parent lists it as one.</summary>
	private void ConvertToFile(DirectoryWork work, long? fileSize)
	{
		WebDirectory webDirectory = work.Directory;

		logger.LogWarning("Treated '{url}' as file instead of directory ({size})", webDirectory.Url, Helpers.FileSizeHelper.ToHumanReadable(fileSize));

		if (webDirectory.ParentDirectory is not WebDirectory parent)
		{
			return;
		}

		WebFile file = parent.ReplaceSubdirectoryWithFile(webDirectory, fileSize);
		session.Counters.AddFiles(1, fileSize ?? 0);
		scanDatabase.MirrorDirectoryBecameFile(webDirectory, file, parent);
	}

	private async Task FileWorkerAsync(string workerName, CancellationToken cancellationToken)
	{
		await foreach (WebFile webFile in _files.Reader.ReadAllAsync(cancellationToken))
		{
			Interlocked.Decrement(ref _queuedFiles);
			Interlocked.Increment(ref _runningFileWorkers);

			try
			{
				logger.LogDebug("[{thread}] Retrieve filesize for: {url}", workerName, webFile.Url);

				long previousFileSize = webFile.FileSize ?? 0;

				webFile.FileSize = await fileSizeLookup.GetFileSizeAsync(webFile.Url, cancellationToken) ?? 0;

				// Counted (as 0 or an estimate) when its directory was added, only the difference is new
				session.Counters.AddFileSize(webFile.FileSize.Value - previousFileSize);

				scanDatabase.MirrorFileSize(webFile);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error retrieving filesize of Url: '{url}'", webFile.Url);
			}
			finally
			{
				Interlocked.Decrement(ref _runningFileWorkers);

				if (!cancellationToken.IsCancellationRequested)
				{
					// Done or failed, it is not outstanding anymore: else its directory would never close
					WebDirectory.TryClose(webFile.ParentDirectory);
				}

				if (Interlocked.Decrement(ref _outstandingFiles) == 0)
				{
					_files.Writer.TryComplete();
				}
			}
		}
	}

	/// <summary>Plain loop: a directory can hold 100k+ files</summary>
	private static long SumFileSizes(List<WebFile> files)
	{
		long total = 0;

		foreach (WebFile file in files)
		{
			total += file.FileSize ?? 0;
		}

		return total;
	}
}
