using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Crawling;
using OpenDirectoryDownloader.Outputs;
using OpenDirectoryDownloader.Reporting;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using OpenDirectoryDownloader.Urls;
using System.Reflection;
using System.Threading.Channels;

namespace OpenDirectoryDownloader.Hosting;

/// <summary>
/// The keys that work during a scan (I, S, T, U, J, P, C, ESC/X). A dedicated thread waits for keys, the commands
/// run here; exiting stops the host like Ctrl+C does, so everything is closed and saved properly.
/// </summary>
public sealed class ConsoleCommandService(
	ScanSession session,
	ScanDatabase scanDatabase,
	ScanDatabaseFile databaseFile,
	CrawlEngine crawlEngine,
	ScanController controller,
	SessionJsonFactory sessionJsonFactory,
	OutputPaths outputPaths,
	IHostApplicationLifetime lifetime,
	ILogger<ConsoleCommandService> logger) : BackgroundService
{
	private static readonly string VersionNumber = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";

	public static void ShowInfoAndCommands()
	{
		Console.WriteLine(
			"┌─────────────────────────────────────────────────────────────────────────┐\n" +
			$"│ KoalaBear84/OpenDirectoryDownloader v{VersionNumber}{string.Empty.PadLeft(Math.Max(0, 36 - VersionNumber.Length - 1))}│\n" +
			"├─────────────────────────────────────────────────────────────────────────┤\n" +
			"│ Press I for info (this)                                                 │\n" +
			"│ Press S for statistics                                                  │\n" +
			"│ Press T for thread info                                                 │\n" +
			"│ Press U for Save TXT                                                    │\n" +
			"│ Press J for Save JSON                                                   │\n" +
			"│ Press P to Pause (stop cleanly, resumable with --resume)                │\n" +
			"├─────────────────────────────────────────────────────────────────────────┤\n" +
			"│ Press ESC or X to EXIT                                                  │\n" +
			"└─────────────────────────────────────────────────────────────────────────┘\n");
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		Channel<char> keys = Channel.CreateUnbounded<char>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

		if (Console.IsInputRedirected)
		{
			const string message = "Console input is redirected, maybe it is run inside another host. This could mean that no input will be send/processed.";
			Console.WriteLine(message);
			logger.LogWarning(message);
		}

		// Blocking reads, on their own background thread so they never hold up a pool thread or the exit
		Thread readerThread = new(() => ReadKeys(keys.Writer, stoppingToken))
		{
			IsBackground = true,
			Name = "Console keys"
		};

		readerThread.Start();

		try
		{
			await foreach (char key in keys.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await HandleAsync(key, stoppingToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Error processing action");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Stopping
		}
	}

	private void ReadKeys(ChannelWriter<char> writer, CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				char key;

				if (Console.IsInputRedirected)
				{
					int read = Console.Read();

					if (read == -1)
					{
						// End of input: nothing will ever come
						return;
					}

					key = (char)read;
				}
				else
				{
					ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
					key = keyInfo.Key == ConsoleKey.Escape ? (char)27 : keyInfo.KeyChar;
				}

				writer.TryWrite(char.ToUpperInvariant(key));
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			// Detached (nohup ... &): stdin can be a closed descriptor that throws instead of returning end of input
			logger.LogWarning(ex, "Console input is unavailable (e.g. closed stdin when running detached); ignoring console input from now on.");
		}
		finally
		{
			writer.TryComplete();
		}
	}

	private async Task HandleAsync(char key, CancellationToken cancellationToken)
	{
		switch (key)
		{
			case 'X':
			case (char)27:
				Exit();
				break;
			case 'I':
				ShowInfoAndCommands();
				break;
			case 'S':
				Console.WriteLine(Statistics.GetSessionStats(session, await GetReportAsync(), includeExtensions: true));
				Console.WriteLine(ScanWorker.QueueStatus(crawlEngine));
				break;
			case 'T':
				Console.WriteLine("Running threads:");

				foreach (DirectoryWork work in crawlEngine.InProgress.OrderByDescending(work => work.Started))
				{
					Console.WriteLine($"[{work.WorkerName}] {UrlHelper.FormatWithThousands((DateTimeOffset.UtcNow - work.Started).TotalMilliseconds)}ms | {work.Directory.Url}");
				}

				break;
			case 'U':
				await SaveUrlsAsync(cancellationToken);
				break;
			case 'J':
				await SaveJsonAsync(cancellationToken);
				break;
			case 'P':
				await PauseAndExitAsync();
				break;
			case 'C':
				if (controller.Finished)
				{
					if (ClipboardWriter.TrySetText(Statistics.GetSessionStats(session, await GetReportAsync(), includeExtensions: true, onlyRedditStats: true, totalDirectories: session.Counters.DirectoriesFinished)))
					{
						Console.WriteLine("Copied Reddit stats to clipboard!");
					}
					else
					{
						logger.LogError("Error copying stats to clipboard");
					}

					Exit();
				}

				break;
		}
	}

	/// <summary>During the scan from the database, after it the final one: the database may be closed and deleted by then.</summary>
	private async Task<ScanReport> GetReportAsync() => controller.FinalReport ?? await ScanReport.TryCreateAsync(scanDatabase, logger);

	private async Task SaveUrlsAsync(CancellationToken cancellationToken)
	{
		try
		{
			logger.LogInformation("Saving URL list to file..");
			Console.WriteLine("Saving URL list to file..");

			string urlsPath = outputPaths.Get("txt");
			await UrlListOutput.WriteAsync(scanDatabase, urlsPath, cancellationToken);

			logger.LogInformation("Saved URL list to file: {path}", urlsPath);
			Console.WriteLine($"Saved URL list to file: {urlsPath}");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Error saving URLs to file");
		}
	}

	private async Task SaveJsonAsync(CancellationToken cancellationToken)
	{
		try
		{
			await sessionJsonFactory.SaveAsync(cancellationToken: cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Error saving session to JSON");
		}
	}

	/// <summary>Stops the scan cleanly (the P key), saves the database and shows how to continue it.</summary>
	private async Task PauseAndExitAsync()
	{
		try
		{
			Console.WriteLine("Pausing - waiting for in-flight requests and saving the database, please wait...");

			await controller.PauseAsync();

			Console.WriteLine($"Paused. Resume with: --resume --db-path \"{databaseFile.Path}\" -u {session.Root.Url}");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error pausing");
		}
		finally
		{
			Exit();
		}
	}

	private void Exit()
	{
		Console.WriteLine("Exiting...");
		logger.LogInformation("Exiting...");
		lifetime.StopApplication();
	}
}
