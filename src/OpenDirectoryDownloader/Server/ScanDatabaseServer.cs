using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Storage;
using Serilog.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenDirectoryDownloader.Server;

/// <summary>
/// Hosts the local web viewer for a scan database (`--serve`, see 'Web viewer' in the README). Read-only:
/// this never touches the scanning/indexing code path, it only browses an existing (or still-being-written)
/// database produced by --use-database.
/// </summary>
public static class ScanDatabaseServer
{
	/// <summary>How many ports past --serve-port to try before giving up (--serve-port itself counts as the first attempt).</summary>
	private const int MaxPortAttempts = 20;

	/// <summary>Guards <see cref="EnsureShutdownCleanupAsync"/> so it only ever runs once, regardless of which of the (possibly several) shutdown paths below gets there first.</summary>
	private static int _shutdownCleanedUp;

	public static async Task<int> RunAsync(CommandLineOptions options, Logger logger)
	{
		WebApplication app = null;
		ServerState state = null;
		HttpClient httpClient = null;
		int port = options.ServePort;

		for (int attempt = 0; attempt < MaxPortAttempts; attempt++)
		{
			WebApplicationBuilder builder = WebApplication.CreateBuilder();
			builder.Logging.ClearProviders();
			builder.Logging.SetMinimumLevel(LogLevel.Warning);
			builder.WebHost.UseUrls($"http://{options.ServeHost}:{port}");

			app = builder.Build();
			state = new ServerState();
			httpClient = new HttpClient();
			ConcurrentDictionary<string, ZipJobProgress> zipJobs = new();

			MapPages(app);
			MapApi(app, state, httpClient, zipJobs);

			try
			{
				// Split out from the usual RunAsync() so a bind failure (port already in use) can be caught
				// here and retried on the next port, instead of crashing with an unhandled exception.
				await app.StartAsync();
				break;
			}
			catch (IOException)
			{
				await app.DisposeAsync();
				httpClient.Dispose();
				app = null;

				if (attempt == MaxPortAttempts - 1)
				{
					break;
				}

				logger.Warning("Port {port} is already in use, trying {nextPort}", port, port + 1);
				port++;
			}
		}

		if (app is null)
		{
			Console.WriteLine($"Could not find a free port to listen on (tried {options.ServePort}-{port}).");
			return 1;
		}

		if (port != options.ServePort)
		{
			Console.WriteLine($"Port {options.ServePort} was already in use - listening on {port} instead.");
		}

		if (!string.IsNullOrWhiteSpace(options.ServeDb))
		{
			try
			{
				await state.LoadAsync(Path.GetFullPath(options.ServeDb), isTempFile: false, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Could not load '{options.ServeDb}': {ex.Message}");
				logger.Warning(ex, "Could not preload database '{path}'", options.ServeDb);
			}
		}

		string url = $"http://{options.ServeHost}:{port}/";

		Console.WriteLine($"Web viewer listening on {url}");
		Console.WriteLine("Drag a .sqlite scan database onto the page to view it. Press Ctrl+C to stop.");
		logger.Information("Web viewer listening on {url}", url);

		if (!options.ServeNoBrowser)
		{
			try
			{
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			}
			catch (Exception ex)
			{
				logger.Warning(ex, "Could not automatically open the browser");
			}
		}

		// Belt-and-braces alongside WaitForShutdownAsync() below: the generic host's own ConsoleLifetime
		// already subscribes to this to trigger a graceful stop, so this is mostly a safety net for process
		// exit paths that bypass that (it doesn't, by itself, fix clicking the console window's close button
		// - see RegisterWindowsCloseCleanup for why that needs the native handler instead).
		AppDomain.CurrentDomain.ProcessExit += (_, _) => EnsureShutdownCleanupAsync(state, httpClient).GetAwaiter().GetResult();

		RegisterWindowsCloseCleanup(state, httpClient);

		await app.WaitForShutdownAsync();

		await EnsureShutdownCleanupAsync(state, httpClient);

		return 0;
	}

	/// <summary>Disposes the loaded database (see ScanDatabase.OpenReadOnlyAsync for why that matters - it's what cleans up its -wal/-shm sidecar files) and the shared HttpClient exactly once, however shutdown was triggered.</summary>
	private static async Task EnsureShutdownCleanupAsync(ServerState state, HttpClient httpClient)
	{
		if (Interlocked.Exchange(ref _shutdownCleanedUp, 1) != 0)
		{
			return;
		}

		await state.DisposeAsync();
		httpClient.Dispose();
	}

	/// <summary>
	/// Clicking the X on the console window (or signing out, or shutting down) sends CTRL_CLOSE_EVENT /
	/// CTRL_LOGOFF_EVENT / CTRL_SHUTDOWN_EVENT - none of which Console.CancelKeyPress ever raises (that's
	/// CTRL_C_EVENT/CTRL_BREAK_EVENT only), and none of which reliably let AppDomain.ProcessExit run to
	/// completion either, since Windows force-terminates the process shortly after one of these fires
	/// regardless of what any handler does. Without a handler registered specifically for these, the process
	/// is simply killed - same as any other forceful kill - skipping ServerState.DisposeAsync() and leaving
	/// the loaded database's -wal/-shm sidecar files behind. SetConsoleCtrlHandler is the one mechanism
	/// Windows actually blocks on (briefly) before terminating, so cleanup here is done synchronously rather
	/// than fired-and-forgotten.
	/// </summary>
	private static void RegisterWindowsCloseCleanup(ServerState state, HttpClient httpClient)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		// Stored in a field, not just passed to SetConsoleCtrlHandler, so the delegate isn't eligible for GC
		// for as long as the native registration holds a pointer to it.
		_consoleCtrlHandler = ctrlType =>
		{
			if (ctrlType is CtrlCloseEvent or CtrlLogoffEvent or CtrlShutdownEvent)
			{
				EnsureShutdownCleanupAsync(state, httpClient).GetAwaiter().GetResult();
			}

			return false;
		};

		NativeMethods.SetConsoleCtrlHandler(_consoleCtrlHandler, add: true);
	}

	private const uint CtrlCloseEvent = 2;
	private const uint CtrlLogoffEvent = 5;
	private const uint CtrlShutdownEvent = 6;

	private delegate bool ConsoleCtrlHandler(uint ctrlType);

	private static ConsoleCtrlHandler _consoleCtrlHandler;

	private static class NativeMethods
	{
		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);
	}

	/// <summary>
	/// Serves the viewer's page/script/stylesheet directly from string constants compiled into the assembly
	/// (see WebAssets.cs) - no wwwroot, no static-file middleware, and nothing left behind if the .exe is
	/// copied on its own (e.g. self-contained single-file publish, or dragging just the .exe elsewhere).
	/// Every other path (breadcrumb/back-forward navigation into a directory) is also served this same
	/// index.html - the client renders the right view from the URL via the History API (see app.js).
	/// </summary>
	private static void MapPages(WebApplication app)
	{
		app.MapGet("/", () => Results.Content(WebAssets.IndexHtml, "text/html; charset=utf-8"));
		app.MapGet("/app.js", () => Results.Content(WebAssets.AppJs, "text/javascript; charset=utf-8"));
		app.MapGet("/style.css", () => Results.Content(WebAssets.StyleCss, "text/css; charset=utf-8"));
	}

	private static void MapApi(WebApplication app, ServerState state, HttpClient httpClient, ConcurrentDictionary<string, ZipJobProgress> zipJobs)
	{
		app.MapGet("/api/status", async (CancellationToken cancellationToken) =>
		{
			ServerStatus status = await state.GetStatusAsync(cancellationToken);

			if (!status.Loaded)
			{
				return Results.Ok(new { loaded = false });
			}

			return Results.Ok(new
			{
				loaded = true,
				rootUrl = status.ScanInfo?.RootUrl,
				firstStartedAtUtc = status.ScanInfo?.FirstStartedAtUtc,
				completedAtUtc = status.ScanInfo?.CompletedAtUtc,
				runCount = status.RunCount,
				parserTypes = status.ParserTypes,
				totalFiles = status.OverallStats.TotalFiles,
				totalDirectories = status.OverallStats.TotalDirectories,
				totalSize = status.OverallStats.TotalSize,
				databaseSizeBytes = status.DatabaseSizeBytes
			});
		});

		app.MapGet("/api/stats", async (CancellationToken cancellationToken) =>
		{
			try
			{
				// Reads every file's name/size to group by extension (see Statistics.GetExtensionsAsync) -
				// unlike every other endpoint here, this is a full table scan, not an indexed lookup. Fine
				// for an explicit, occasional "Statistics" button click; not something to call on every
				// page load the way /api/status is.
				Dictionary<string, ExtensionStats> extensionStats = await state.QueryAsync(Statistics.GetExtensionsAsync, cancellationToken);
				ScanDatabase.SpeedtestSnapshot speedtest = await state.QueryAsync(db => db.GetLatestSpeedtestResultAsync(), cancellationToken);

				return Results.Ok(new
				{
					extensions = extensionStats
						.OrderByDescending(pair => pair.Value.Count)
						.Select(pair => new
						{
							extension = pair.Key,
							fileCount = pair.Value.Count,
							totalSize = pair.Value.FileSize
						}),
					speedtest = speedtest is null ? null : new
					{
						downloadedBytes = speedtest.DownloadedBytes,
						elapsedMilliseconds = speedtest.ElapsedMilliseconds,
						maxBytesPerSecond = speedtest.MaxBytesPerSecond
					}
				});
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		});

		app.MapPost("/api/database", async (HttpRequest request, string fileName, CancellationToken cancellationToken) =>
		{
			string safeFileName = Path.GetFileName(fileName);

			if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
			{
				return Results.BadRequest(new { error = "Only .sqlite files are supported." });
			}

			string tempDirectory = Path.Combine(Path.GetTempPath(), "OpenDirectoryDownloader-Serve");
			Directory.CreateDirectory(tempDirectory);

			string tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}-{safeFileName}");

			await using (FileStream fileStream = File.Create(tempPath))
			{
				await request.Body.CopyToAsync(fileStream, cancellationToken);
			}

			try
			{
				await state.LoadAsync(tempPath, isTempFile: true, cancellationToken);
			}
			catch (Exception ex)
			{
				File.Delete(tempPath);
				return Results.BadRequest(new { error = ex.Message });
			}

			return Results.Ok(new { loaded = true });
		});

		app.MapGet("/api/directory", async (string url, CancellationToken cancellationToken) =>
		{
			try
			{
				ScanDatabase.MirroredDirectory self = await state.QueryAsync(db => db.GetDirectoryAsync(url), cancellationToken);

				if (self is null)
				{
					return Results.NotFound(new { error = $"'{url}' was not found in the loaded database." });
				}

				List<ScanDatabase.MirroredFile> ownFiles = await state.QueryAsync(db => db.GetFilesAsync(url), cancellationToken);
				List<ScanDatabase.MirroredDirectory> subdirectories = await state.QueryAsync(db => db.GetSubdirectoriesAsync(url), cancellationToken);
				Dictionary<string, ScanDatabase.SubtreeAggregate> aggregates = await state.QueryAsync(db => db.GetChildSubtreeAggregatesAsync(url), cancellationToken);
				List<(string Url, string Name)> ancestors = await state.QueryAsync(db => db.GetAncestorsAsync(url), cancellationToken);

				long ownFilesSize = ownFiles.Sum(file => file.FileSize ?? 0);
				long subdirectoriesSize = aggregates.Values.Sum(aggregate => aggregate.TotalSize);

				List<object> entries = [];

				foreach (ScanDatabase.MirroredDirectory directory in subdirectories)
				{
					aggregates.TryGetValue(directory.Url, out ScanDatabase.SubtreeAggregate aggregate);

					entries.Add(new
					{
						type = "directory",
						url = directory.Url,
						name = directory.Name,
						finished = directory.Finished,
						error = directory.Error,
						size = aggregate?.TotalSize ?? 0,
						fileCount = aggregate?.FileCount ?? 0,
						directoryCount = aggregate?.DirectoryCount ?? 0,
						unknownSizeCount = aggregate?.UnknownSizeCount ?? 0
					});
				}

				foreach (ScanDatabase.MirroredFile file in ownFiles)
				{
					entries.Add(new
					{
						type = "file",
						url = file.Url,
						name = file.FileName,
						size = file.FileSize ?? 0,
						unknownSize = file.FileSize is null,
						description = file.Description
					});
				}

				return Results.Ok(new
				{
					url,
					name = self.Name,
					parentUrl = self.ParentUrl,
					totalSize = ownFilesSize + subdirectoriesSize,
					ancestors = ancestors.Select(ancestor => new { url = ancestor.Url, name = ancestor.Name }),
					entries
				});
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		});

		app.MapGet("/api/search", async (string q, int? limit, CancellationToken cancellationToken) =>
		{
			string term = q?.Trim();

			if (string.IsNullOrEmpty(term) || term.Length < 2)
			{
				return Results.BadRequest(new { error = "Search term must be at least 2 characters." });
			}

			try
			{
				ScanDatabase.SearchResult result = await state.QueryAsync(db => db.SearchAsync(term, limit is > 0 ? limit.Value : 200), cancellationToken);

				return Results.Ok(new
				{
					files = result.Files.Select(file => new
					{
						url = file.Url,
						directoryUrl = file.DirectoryUrl,
						name = file.FileName,
						size = file.FileSize ?? 0,
						unknownSize = file.FileSize is null,
						description = file.Description
					}),
					directories = result.Directories.Select(directory => new
					{
						url = directory.Url,
						parentUrl = directory.ParentUrl,
						name = directory.Name,
						finished = directory.Finished,
						error = directory.Error
					}),
					truncated = result.Truncated
				});
			}
			catch (InvalidOperationException ex)
			{
				return Results.BadRequest(new { error = ex.Message });
			}
		});

		app.MapGet("/api/download-file", async (HttpContext context, string url, CancellationToken cancellationToken) =>
		{
			ScanDatabase.FileInfoSnapshot fileInfo;

			try
			{
				fileInfo = await state.QueryAsync(db => db.GetFileInfoAsync(url), cancellationToken);
			}
			catch (InvalidOperationException ex)
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				await context.Response.WriteAsJsonAsync(new { error = ex.Message }, cancellationToken);
				return;
			}

			// Prefer the name recorded during the scan - deriving one from the URL alone breaks for sites
			// like Google Drive, whose download URLs (".../uc?id=...") carry no filename at all.
			string fileName = fileInfo?.FileName ?? GetFileNameFromUrl(url);
			long? knownSize = fileInfo?.FileSize;
			string contentType = GetContentType(fileName);

			using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				context.Response.StatusCode = StatusCodes.Status502BadGateway;
				await context.Response.WriteAsJsonAsync(new { error = $"Remote server returned {(int)response.StatusCode}." }, cancellationToken);
				return;
			}

			context.Response.ContentType = contentType;
			context.Response.Headers.ContentDisposition = BuildContentDisposition(fileName);

			long? contentLength = knownSize ?? response.Content.Headers.ContentLength;

			if (contentLength.HasValue)
			{
				context.Response.ContentLength = contentLength;
			}

			await using Stream remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);
			await remoteStream.CopyToAsync(context.Response.Body, cancellationToken);
		});

		app.MapGet("/api/download-zip", async (HttpContext context, string url, string jobId, CancellationToken cancellationToken) =>
		{
			// Registered immediately, before any await, so the progress endpoint (which the client opens
			// right after kicking off this request) finds the job as soon as possible.
			ZipJobProgress progress = new();
			zipJobs[jobId] = progress;

			ScanDatabase.MirroredDirectory directory;

			try
			{
				directory = await state.QueryAsync(db => db.GetDirectoryAsync(url), cancellationToken);
			}
			catch (InvalidOperationException ex)
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				await context.Response.WriteAsJsonAsync(new { error = ex.Message }, cancellationToken);
				return;
			}

			if (directory is null)
			{
				context.Response.StatusCode = StatusCodes.Status404NotFound;
				await context.Response.WriteAsJsonAsync(new { error = $"'{url}' was not found in the loaded database." }, cancellationToken);
				return;
			}

			// System.IO.Compression.ZipArchive does synchronous I/O against its destination stream when an
			// entry is closed (flushing/writing the data descriptor) - Kestrel disallows synchronous writes
			// on the response body by default (it can deadlock the connection), so it must be opted back in
			// for this one streamed response.
			Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature syncIOFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();

			syncIOFeature?.AllowSynchronousIO = true;

			context.Response.ContentType = "application/zip";
			context.Response.Headers.ContentDisposition = BuildContentDisposition($"{directory.Name}.zip");

			try
			{
				ScanDatabase.SubtreeResult subtree = await state.QueryAsync(db => db.GetSubtreeAsync(url), cancellationToken);

				await DirectoryZipService.WriteZipAsync(subtree, url, directory.Name, httpClient, context.Response.Body, progress, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				progress.ErrorMessage = ex.Message;
				Program.Logger.Error(ex, "Failed building a ZIP of '{url}'", url);
			}
			finally
			{
				progress.IsComplete = true;

				_ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(completedTask => zipJobs.TryRemove(jobId, out _), TaskScheduler.Default);
			}
		});

		app.MapGet("/api/zip-progress/{jobId}", async (HttpContext context, string jobId, CancellationToken cancellationToken) =>
		{
			context.Response.Headers.ContentType = "text/event-stream";
			context.Response.Headers.CacheControl = "no-cache";
			context.Response.Headers["X-Accel-Buffering"] = "no";

			// The client opens this connection right after kicking off the download-zip request; that
			// request registers the job as its very first action, but the two requests can still race
			// (e.g. across independent connections), so give it a brief grace period before giving up.
			int missingAttempts = 0;

			while (!cancellationToken.IsCancellationRequested)
			{
				if (!zipJobs.TryGetValue(jobId, out ZipJobProgress progress))
				{
					if (missingAttempts++ < 20)
					{
						await Task.Delay(100, cancellationToken);
						continue;
					}

					await WriteSseEventAsync(context.Response, new { error = "Unknown or expired job." }, cancellationToken);
					return;
				}

				bool isComplete = progress.IsComplete;

				await WriteSseEventAsync(context.Response, new
				{
					filesDone = progress.FilesDone,
					totalFiles = progress.TotalFiles,
					bytesWritten = progress.BytesWritten,
					currentFile = progress.CurrentFile,
					skipped = progress.Skipped,
					isComplete,
					error = progress.ErrorMessage
				}, cancellationToken);

				if (isComplete)
				{
					return;
				}

				await Task.Delay(250, cancellationToken);
			}
		});
	}

	private static async Task WriteSseEventAsync(HttpResponse response, object payload, CancellationToken cancellationToken)
	{
		string json = JsonSerializer.Serialize(payload);

		await response.WriteAsync($"data: {json}\n\n", cancellationToken);
		await response.Body.FlushAsync(cancellationToken);
	}

	private static string GetFileNameFromUrl(string url)
	{
		string fileName = null;

		try
		{
			fileName = Path.GetFileName(Uri.UnescapeDataString(new Uri(url).LocalPath));
		}
		catch (UriFormatException)
		{
			// Fall through to the "download" default below.
		}

		return string.IsNullOrEmpty(fileName) ? "download" : fileName;
	}

	/// <summary>
	/// A small hand-rolled extension lookup instead of Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider
	/// (that type exists to drive static-file middleware serving from disk, which this server deliberately
	/// doesn't use) - this is only ever a Content-Type hint for a proxied download, so it doesn't need to be exhaustive.
	/// </summary>
	private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
	{
		[".htm"] = "text/html",
		[".html"] = "text/html",
		[".txt"] = "text/plain",
		[".csv"] = "text/csv",
		[".md"] = "text/markdown",
		[".json"] = "application/json",
		[".xml"] = "application/xml",
		[".pdf"] = "application/pdf",
		[".zip"] = "application/zip",
		[".rar"] = "application/vnd.rar",
		[".7z"] = "application/x-7z-compressed",
		[".gz"] = "application/gzip",
		[".tar"] = "application/x-tar",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".png"] = "image/png",
		[".gif"] = "image/gif",
		[".webp"] = "image/webp",
		[".bmp"] = "image/bmp",
		[".svg"] = "image/svg+xml",
		[".avif"] = "image/avif",
		[".ico"] = "image/x-icon",
		[".mp4"] = "video/mp4",
		[".mkv"] = "video/x-matroska",
		[".webm"] = "video/webm",
		[".avi"] = "video/x-msvideo",
		[".mov"] = "video/quicktime",
		[".mp3"] = "audio/mpeg",
		[".wav"] = "audio/wav",
		[".flac"] = "audio/flac",
		[".ogg"] = "audio/ogg",
		[".m4a"] = "audio/mp4",
	};

	private static string GetContentType(string fileName)
	{
		string extension = Path.GetExtension(fileName);

		return !string.IsNullOrEmpty(extension) && ContentTypesByExtension.TryGetValue(extension, out string contentType)
			? contentType
			: "application/octet-stream";
	}

	private static string BuildContentDisposition(string fileName)
	{
		string asciiFallback = new(fileName.Where(c => c >= 0x20 && c < 0x7F && c != '"').ToArray());

		if (string.IsNullOrEmpty(asciiFallback))
		{
			asciiFallback = "download";
		}

		return $"attachment; filename=\"{asciiFallback}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
	}

	private sealed record ServerStatus(bool Loaded, ScanDatabase.ScanInfoSnapshot ScanInfo, long RunCount, List<string> ParserTypes, ScanDatabase.OverallStats OverallStats, long DatabaseSizeBytes);

	/// <summary>
	/// The single "currently loaded database" for this server (a local, single-user tool - one database at
	/// a time is enough). All access to the underlying ScanDatabase/SqliteConnection is serialized through
	/// one gate: Microsoft.Data.Sqlite connections aren't safe for concurrent use from multiple threads, and
	/// the ASP.NET Core request pipeline will happily run several requests (e.g. two directory listings, or
	/// a listing alongside a database swap) at once. Read queries are cheap, so coarse serialization here is
	/// not a real bottleneck. The ZIP endpoint only holds the gate for its brief GetSubtreeAsync call - the
	/// actual (potentially long) file fetching/zipping afterward runs lock-free against the already-fetched
	/// in-memory subtree.
	/// </summary>
	private sealed class ServerState : IAsyncDisposable
	{
		private readonly SemaphoreSlim _gate = new(1, 1);
		private ScanDatabase _database;
		private ScanDatabase.ScanInfoSnapshot _scanInfo;
		private long _runCount;
		private List<string> _parserTypes = [];
		private ScanDatabase.OverallStats _overallStats = new(0, 0, 0);
		private long _databaseSizeBytes;
		private string _loadedPath;
		private bool _isTempFile;

		public async Task LoadAsync(string path, bool isTempFile, CancellationToken cancellationToken)
		{
			ScanDatabase newDatabase = await ScanDatabase.OpenReadOnlyAsync(path, cancellationToken);
			ScanDatabase.ScanInfoSnapshot scanInfo = await newDatabase.GetScanInfoAsync();
			long runCount = await newDatabase.CountRunsAsync();
			List<string> parserTypes = await newDatabase.GetDistinctParserTypesAsync();
			ScanDatabase.OverallStats overallStats = await newDatabase.GetOverallStatsAsync();
			long databaseSizeBytes = new FileInfo(path).Length;

			await _gate.WaitAsync(cancellationToken);
			try
			{
				ScanDatabase previousDatabase = _database;
				string previousPath = _loadedPath;
				bool previousWasTemp = _isTempFile;

				_database = newDatabase;
				_scanInfo = scanInfo;
				_runCount = runCount;
				_parserTypes = parserTypes;
				_overallStats = overallStats;
				_databaseSizeBytes = databaseSizeBytes;
				_loadedPath = path;
				_isTempFile = isTempFile;

				if (previousDatabase is not null)
				{
					await previousDatabase.DisposeAsync();

					if (previousWasTemp)
					{
						DeleteTempDatabaseFiles(previousPath);
					}
				}
			}
			finally
			{
				_gate.Release();
			}
		}

		/// <summary>Deletes a temp-uploaded database's main file and its WAL/SHM sidecar files, if any (SQLite creates these for a WAL-mode database even when opened read-only).</summary>
		private static void DeleteTempDatabaseFiles(string path)
		{
			if (path is null)
			{
				return;
			}

			foreach (string candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
			{
				if (File.Exists(candidate))
				{
					File.Delete(candidate);
				}
			}
		}

		public async Task<T> QueryAsync<T>(Func<ScanDatabase, Task<T>> query, CancellationToken cancellationToken)
		{
			await _gate.WaitAsync(cancellationToken);
			try
			{
				if (_database is null)
				{
					throw new InvalidOperationException("No database is loaded yet - drag a .sqlite file onto the page.");
				}

				return await query(_database);
			}
			finally
			{
				_gate.Release();
			}
		}

		public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken)
		{
			await _gate.WaitAsync(cancellationToken);
			try
			{
				return new ServerStatus(_database is not null, _scanInfo, _runCount, _parserTypes, _overallStats, _databaseSizeBytes);
			}
			finally
			{
				_gate.Release();
			}
		}

		public async ValueTask DisposeAsync()
		{
			if (_database is not null)
			{
				await _database.DisposeAsync();
			}

			if (_isTempFile)
			{
				DeleteTempDatabaseFiles(_loadedPath);
			}
		}
	}
}
