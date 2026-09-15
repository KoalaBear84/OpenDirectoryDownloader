using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using OpenDirectoryDownloader.Shared.Models;
using System.Threading.Channels;

namespace OpenDirectoryDownloader.Storage;

/// <summary>
/// Mirrors discovered directories and files to a SQLite database as a scan progresses (issue #56).
/// Phase 1 only: this is write-through, additive, best-effort mirroring. The in-memory WebDirectory
/// tree keeps working exactly as it does today, and nothing reads from this database yet. Writes are
/// batched through a single writer task so callers on the crawl threads never block on disk I/O.
/// </summary>
public sealed class ScanDatabase : IAsyncDisposable
{
	private const int MaxBatchSize = 1000;

	private readonly SqliteConnection _connection;
	private readonly Channel<IScanRecord> _channel;
	private readonly Task _writerTask;

	/// <summary>
	/// The ScanRuns row this process is currently updating (issue #56 phase 6+: one row per --resume
	/// attempt). Set once by BeginRunAsync, near the very start of a scan, before any crawling (and so
	/// before any MirrorSessionStats call) can happen - read directly by MirrorSessionStats afterward, safe
	/// without further synchronization since nothing writes it again after that single assignment.
	/// </summary>
	private int _currentRunNumber;

	public string Path { get; }

	private ScanDatabase(SqliteConnection connection, string path)
	{
		_connection = connection;
		Path = path;

		_channel = Channel.CreateUnbounded<IScanRecord>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false
		});

		_writerTask = Task.Run(WriterLoopAsync);
	}

	public static Task<ScanDatabase> CreateAsync(string path, CancellationToken cancellationToken = default) =>
		OpenAsync(path, deleteExisting: true, cancellationToken);

	/// <summary>
	/// Opens an existing database file to continue a previously interrupted scan (issue #56 phase 6:
	/// resume), without deleting it first. Callers are expected to have already checked the file exists -
	/// if it doesn't, this creates a fresh, empty one (same as CreateAsync).
	/// </summary>
	public static Task<ScanDatabase> OpenForResumeAsync(string path, CancellationToken cancellationToken = default) =>
		OpenAsync(path, deleteExisting: false, cancellationToken);

	private static async Task<ScanDatabase> OpenAsync(string path, bool deleteExisting, CancellationToken cancellationToken)
	{
		string directory = System.IO.Path.GetDirectoryName(path);

		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		if (deleteExisting && File.Exists(path))
		{
			File.Delete(path);
		}

		SqliteConnection connection = new($"Data Source={path}");
		await connection.OpenAsync(cancellationToken);

		using (SqliteCommand pragmaAndSchemaCommand = connection.CreateCommand())
		{
			pragmaAndSchemaCommand.CommandText =
				"""
				PRAGMA journal_mode = WAL;
				PRAGMA synchronous = NORMAL;

				CREATE TABLE IF NOT EXISTS Directories (
					Url TEXT PRIMARY KEY,
					ParentUrl TEXT NULL,
					Name TEXT NOT NULL,
					Description TEXT NULL,
					Finished INTEGER NOT NULL,
					Error INTEGER NOT NULL
				);

				CREATE INDEX IF NOT EXISTS IX_Directories_ParentUrl ON Directories (ParentUrl);

				CREATE TABLE IF NOT EXISTS Files (
					Url TEXT PRIMARY KEY,
					DirectoryUrl TEXT NOT NULL,
					FileName TEXT NOT NULL,
					FileSize INTEGER NULL,
					Description TEXT NULL
				);

				CREATE INDEX IF NOT EXISTS IX_Files_DirectoryUrl ON Files (DirectoryUrl);

				CREATE TABLE IF NOT EXISTS ScanInfo (
					Id INTEGER PRIMARY KEY CHECK (Id = 1),
					RootUrl TEXT NOT NULL,
					FirstStartedAtUtc TEXT NOT NULL,
					CompletedAtUtc TEXT NULL
				);

				CREATE TABLE IF NOT EXISTS ScanRuns (
					RunNumber INTEGER PRIMARY KEY AUTOINCREMENT,
					StartedAtUtc TEXT NOT NULL,
					LastUpdatedAtUtc TEXT NOT NULL,
					TotalHttpTraffic INTEGER NOT NULL,
					TotalHttpRequests INTEGER NOT NULL,
					Errors INTEGER NOT NULL,
					Skipped INTEGER NOT NULL,
					HttpStatusCodesJson TEXT NOT NULL
				);
				""";

			await pragmaAndSchemaCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		return new ScanDatabase(connection, path);
	}

	/// <summary>Queues a directory (or an update to a previously queued one, e.g. once it's Finished) for mirroring. Non-blocking.</summary>
	public void MirrorDirectory(WebDirectory webDirectory)
	{
		_channel.Writer.TryWrite(new DirectoryRecord(
			webDirectory.Url,
			webDirectory.ParentDirectory?.Url,
			webDirectory.Name,
			webDirectory.Description,
			webDirectory.Finished,
			webDirectory.Error));
	}

	/// <summary>Queues a file discovered under <paramref name="parentDirectory"/> for mirroring. Non-blocking.</summary>
	public void MirrorFile(WebFile webFile, WebDirectory parentDirectory)
	{
		_channel.Writer.TryWrite(new FileRecord(
			webFile.Url,
			parentDirectory.Url,
			webFile.FileName,
			webFile.FileSize,
			webFile.Description));
	}

	/// <summary>Queues a file size update, e.g. after a HEAD-request lookup completes. Non-blocking.</summary>
	public void MirrorFileSize(WebFile webFile)
	{
		_channel.Writer.TryWrite(new FileSizeRecord(webFile.Url, webFile.FileSize));
	}

	/// <summary>
	/// Records the start of a new attempt at this scan (issue #56 phase 6+): the original scan is run #1,
	/// each --resume afterward is #2, #3, and so on. If <paramref name="isFreshScan"/>, also records the
	/// one-time ScanInfo row (root URL, true first-started time). Must be called once, near the very start
	/// of a scan/resume, before anything else touches this database - MirrorSessionStats needs the
	/// RunNumber this returns (kept internally) to know which ScanRuns row to keep updating, and nothing
	/// before this point may safely write to the connection directly (see the class remarks on ordering).
	/// <paramref name="session"/>'s current counters seed the new run's starting values - zero for a fresh
	/// scan, or whatever --resume already restored from the previous run's last snapshot otherwise - so
	/// each run's numbers are a genuine running total, just historized instead of overwritten in place.
	/// </summary>
	public async Task<int> BeginRunAsync(Session session, string rootUrl, bool isFreshScan)
	{
		TaskCompletionSource<int> runNumberResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
		string httpStatusCodesJson = JsonConvert.SerializeObject(session.HttpStatusCodes);

		_channel.Writer.TryWrite(new BeginRunRecord(
			isFreshScan,
			rootUrl,
			DateTimeOffset.UtcNow,
			session.TotalHttpTraffic,
			session.TotalHttpRequests,
			session.Errors,
			session.Skipped,
			httpStatusCodesJson,
			runNumberResult));

		int runNumber = await runNumberResult.Task;
		_currentRunNumber = runNumber;

		return runNumber;
	}

	/// <summary>
	/// Queues a snapshot of the session's running counters (HTTP traffic/requests, status codes, errors,
	/// skipped) for mirroring, against the run BeginRunAsync started. Non-blocking. These change on
	/// essentially every processed directory, so callers should snapshot periodically (see
	/// OpenDirectoryIndexer.TimerStatistics_Elapsed) rather than on every single increment - each snapshot
	/// overwrites the current run's row, bumping its LastUpdatedAtUtc. On --resume, the previous run's last
	/// snapshot is loaded back (GetLatestRunStatsAsync) so these counters reflect the whole scan, not just
	/// the resumed portion. A no-op before BeginRunAsync has completed (nothing to attribute it to yet).
	/// </summary>
	public void MirrorSessionStats(Session session)
	{
		if (_currentRunNumber == 0)
		{
			return;
		}

		string httpStatusCodesJson = JsonConvert.SerializeObject(session.HttpStatusCodes);

		_channel.Writer.TryWrite(new SessionStatsRecord(_currentRunNumber, DateTimeOffset.UtcNow, session.TotalHttpTraffic, session.TotalHttpRequests, session.Errors, session.Skipped, httpStatusCodesJson));
	}

	/// <summary>Queues the scan's completion time (issue #56 phase 6+). Only meant to be called once, on a genuine full successful finish - not on a pause.</summary>
	public void MarkCompleted(DateTimeOffset completedAtUtc)
	{
		_channel.Writer.TryWrite(new ScanCompletedRecord(completedAtUtc));
	}

	/// <summary>Waits until every queued record so far has been committed to the database.</summary>
	public async Task FlushAsync()
	{
		TaskCompletionSource flushCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

		_channel.Writer.TryWrite(new FlushMarker(flushCompleted));

		await flushCompleted.Task;
	}

	public async Task<long> CountDirectoriesAsync()
	{
		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM Directories";

		return (long)await command.ExecuteScalarAsync();
	}

	public async Task<long> CountFilesAsync()
	{
		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM Files";

		return (long)await command.ExecuteScalarAsync();
	}

	/// <summary>Directories marked Error, whether or not they're also Finished. Used by --resume to ask whether to retry them (issue #56 phase 6).</summary>
	public async Task<long> CountErroredDirectoriesAsync()
	{
		await FlushAsync();

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM Directories WHERE Error = 1";

		return (long)await command.ExecuteScalarAsync();
	}

	/// <summary>How many attempts (ScanRuns rows) exist so far. Call before BeginRunAsync to read it as "N prior attempts", since that call adds one more.</summary>
	public async Task<long> CountRunsAsync()
	{
		await FlushAsync();

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM ScanRuns";

		return (long)await command.ExecuteScalarAsync();
	}

	/// <summary>The scan's one-time identity (root URL, true first-started time, and completion time if it ever finished), or null if BeginRunAsync has never completed for this database (e.g. a database from before this table existed).</summary>
	public async Task<ScanInfoSnapshot> GetScanInfoAsync()
	{
		await FlushAsync();

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT RootUrl, FirstStartedAtUtc, CompletedAtUtc FROM ScanInfo WHERE Id = 1";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new ScanInfoSnapshot(
			reader.GetString(0),
			DateTimeOffset.Parse(reader.GetString(1)),
			reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)));
	}

	public sealed record ScanInfoSnapshot(string RootUrl, DateTimeOffset FirstStartedAtUtc, DateTimeOffset? CompletedAtUtc);

	/// <summary>The most recent attempt's (ScanRuns row's) counters, or null if BeginRunAsync has never completed for this database (a fresh scan, or one interrupted before it could).</summary>
	public async Task<SessionStatsSnapshot> GetLatestRunStatsAsync()
	{
		await FlushAsync();

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText =
			"""
			SELECT RunNumber, StartedAtUtc, LastUpdatedAtUtc, TotalHttpTraffic, TotalHttpRequests, Errors, Skipped, HttpStatusCodesJson
			FROM ScanRuns
			ORDER BY RunNumber DESC
			LIMIT 1
			""";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return null;
		}

		Dictionary<int, int> httpStatusCodes = JsonConvert.DeserializeObject<Dictionary<int, int>>(reader.GetString(7)) ?? [];

		return new SessionStatsSnapshot(
			reader.GetInt32(0),
			DateTimeOffset.Parse(reader.GetString(1)),
			DateTimeOffset.Parse(reader.GetString(2)),
			reader.GetInt64(3),
			reader.GetInt32(4),
			reader.GetInt32(5),
			reader.GetInt32(6),
			httpStatusCodes);
	}

	public sealed record SessionStatsSnapshot(int RunNumber, DateTimeOffset StartedAtUtc, DateTimeOffset LastUpdatedAtUtc, long TotalHttpTraffic, int TotalHttpRequests, int Errors, int Skipped, Dictionary<int, int> HttpStatusCodes);

	public async Task<long?> GetFileSizeAsync(string url)
	{
		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT FileSize FROM Files WHERE Url = $url";
		command.Parameters.AddWithValue("$url", url);

		object result = await command.ExecuteScalarAsync();

		return result is null or DBNull ? null : (long)result;
	}

	/// <summary>
	/// Every file's FileName and FileSize (not Url - callers here only need to group by extension). Used
	/// so Statistics.GetExtensions can produce a per-extension breakdown once eviction may have dropped
	/// some directories' Files from memory (issue #56 phase 4). Flushes pending writes first.
	/// </summary>
	public async Task<List<(string FileName, long? FileSize)>> GetAllFileNamesAndSizesAsync()
	{
		await FlushAsync();

		List<(string, long?)> files = [];

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT FileName, FileSize FROM Files";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			files.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1)));
		}

		return files;
	}

	/// <summary>The single largest file found so far (by FileSize), or null if none have been mirrored yet. Used for --speedtest once eviction may have dropped it from memory (issue #56 phase 4).</summary>
	public async Task<MirroredFile> GetLargestFileAsync()
	{
		await FlushAsync();

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT Url, FileName, FileSize, Description FROM Files ORDER BY FileSize DESC LIMIT 1";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new MirroredFile(
			reader.GetString(0),
			reader.GetString(1),
			reader.IsDBNull(2) ? null : reader.GetInt64(2),
			reader.IsDBNull(3) ? null : reader.GetString(3));
	}

	/// <summary>
	/// Every file's Url, unordered (Files.Url is a primary key, so this is inherently distinct). Used once
	/// eviction may have dropped some directories' Files from memory (issue #56 phase 4), so output that
	/// used to walk the in-memory tree (e.g. the URLs .txt file) can be reconstructed from here instead.
	/// Flushes pending writes first, so it reflects everything mirrored up to the point of calling this.
	/// </summary>
	public async Task<List<string>> GetAllFileUrlsAsync()
	{
		await FlushAsync();

		List<string> urls = [];

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT Url FROM Files";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			urls.Add(reader.GetString(0));
		}

		return urls;
	}

	/// <summary>
	/// Every mirrored directory's full record - used to reconstruct the in-memory tree on --resume (issue
	/// #56 phase 6). Deliberately lightweight (no Files): the whole directory table, even for a scan with
	/// millions of files, is a manageable single result set to hold in memory at once during resume.
	/// Flushes pending writes first.
	/// </summary>
	public async Task<List<MirroredDirectory>> GetAllDirectoriesAsync()
	{
		await FlushAsync();

		List<MirroredDirectory> directories = [];

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT Url, ParentUrl, Name, Description, Finished, Error FROM Directories";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			directories.Add(new MirroredDirectory(
				reader.GetString(0),
				reader.IsDBNull(1) ? null : reader.GetString(1),
				reader.GetString(2),
				reader.IsDBNull(3) ? null : reader.GetString(3),
				reader.GetBoolean(4),
				reader.GetBoolean(5)));
		}

		return directories;
	}

	/// <summary>
	/// Per-directory file counts/sizes/null-size-counts, in one pass - used to reconstruct cached totals
	/// for already-closed subtrees on --resume (issue #56 phase 6) without loading every file into memory.
	/// Keyed by DirectoryUrl; a directory with no files has no entry (treat as all-zero).
	/// </summary>
	public async Task<Dictionary<string, FileAggregate>> GetFileAggregatesByDirectoryAsync()
	{
		await FlushAsync();

		Dictionary<string, FileAggregate> aggregates = [];

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText =
			"""
			SELECT DirectoryUrl, COUNT(*), COALESCE(SUM(FileSize), 0), SUM(CASE WHEN FileSize IS NULL THEN 1 ELSE 0 END)
			FROM Files
			GROUP BY DirectoryUrl
			""";

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			aggregates[reader.GetString(0)] = new FileAggregate(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
		}

		return aggregates;
	}

	public sealed record FileAggregate(long Count, long TotalSize, long NullSizeCount);

	/// <summary>
	/// One directory's own record, or null if it was never mirrored. Used to rebuild JSON output for an
	/// evicted directory - see Library.SaveSessionJson. Does not flush pending writes itself (callers
	/// walking a whole tree should call FlushAsync once up front instead of before every query).
	/// </summary>
	public async Task<MirroredDirectory> GetDirectoryAsync(string url)
	{
		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT ParentUrl, Name, Description, Finished, Error FROM Directories WHERE Url = $url";
		command.Parameters.AddWithValue("$url", url);

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new MirroredDirectory(
			url,
			reader.IsDBNull(0) ? null : reader.GetString(0),
			reader.GetString(1),
			reader.IsDBNull(2) ? null : reader.GetString(2),
			reader.GetBoolean(3),
			reader.GetBoolean(4));
	}

	/// <summary>Direct (non-recursive) subdirectories of <paramref name="parentUrl"/>, ordered for deterministic output.</summary>
	public async Task<List<MirroredDirectory>> GetSubdirectoriesAsync(string parentUrl)
	{
		List<MirroredDirectory> directories = [];

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT Url, Name, Description, Finished, Error FROM Directories WHERE ParentUrl = $parentUrl ORDER BY Url";
		command.Parameters.AddWithValue("$parentUrl", parentUrl);

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			directories.Add(new MirroredDirectory(
				reader.GetString(0),
				parentUrl,
				reader.GetString(1),
				reader.IsDBNull(2) ? null : reader.GetString(2),
				reader.GetBoolean(3),
				reader.GetBoolean(4)));
		}

		return directories;
	}

	/// <summary>Direct (non-recursive) files of <paramref name="directoryUrl"/>, ordered for deterministic output.</summary>
	public async Task<List<MirroredFile>> GetFilesAsync(string directoryUrl)
	{
		List<MirroredFile> files = [];

		using SqliteCommand command = _connection.CreateCommand();
		command.CommandText = "SELECT Url, FileName, FileSize, Description FROM Files WHERE DirectoryUrl = $directoryUrl ORDER BY Url";
		command.Parameters.AddWithValue("$directoryUrl", directoryUrl);

		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			files.Add(new MirroredFile(
				reader.GetString(0),
				reader.GetString(1),
				reader.IsDBNull(2) ? null : reader.GetInt64(2),
				reader.IsDBNull(3) ? null : reader.GetString(3)));
		}

		return files;
	}

	public sealed record MirroredDirectory(string Url, string ParentUrl, string Name, string Description, bool Finished, bool Error);

	public sealed record MirroredFile(string Url, string FileName, long? FileSize, string Description);

	private async Task WriterLoopAsync()
	{
		ChannelReader<IScanRecord> reader = _channel.Reader;
		List<IScanRecord> batch = new(MaxBatchSize);

		try
		{
			while (await reader.WaitToReadAsync())
			{
				while (batch.Count < MaxBatchSize && reader.TryRead(out IScanRecord record))
				{
					batch.Add(record);
				}

				if (batch.Count > 0)
				{
					await WriteBatchAsync(batch);
					batch.Clear();
				}
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error writing to scan database '{path}'", Path);
		}
	}

	private async Task WriteBatchAsync(List<IScanRecord> batch)
	{
		using SqliteTransaction transaction = _connection.BeginTransaction();

		using SqliteCommand upsertDirectoryCommand = CreateUpsertDirectoryCommand(transaction);
		using SqliteCommand upsertFileCommand = CreateUpsertFileCommand(transaction);
		using SqliteCommand updateFileSizeCommand = CreateUpdateFileSizeCommand(transaction);
		using SqliteCommand upsertSessionStatsCommand = CreateUpsertSessionStatsCommand(transaction);

		List<TaskCompletionSource> flushesToComplete = null;

		foreach (IScanRecord record in batch)
		{
			switch (record)
			{
				case DirectoryRecord directoryRecord:
					SetDirectoryParameters(upsertDirectoryCommand, directoryRecord);
					await upsertDirectoryCommand.ExecuteNonQueryAsync();
					break;
				case FileRecord fileRecord:
					SetFileParameters(upsertFileCommand, fileRecord);
					await upsertFileCommand.ExecuteNonQueryAsync();
					break;
				case FileSizeRecord fileSizeRecord:
					SetFileSizeParameters(updateFileSizeCommand, fileSizeRecord);
					await updateFileSizeCommand.ExecuteNonQueryAsync();
					break;
				case SessionStatsRecord sessionStatsRecord:
					SetSessionStatsParameters(upsertSessionStatsCommand, sessionStatsRecord);
					await upsertSessionStatsCommand.ExecuteNonQueryAsync();
					break;
				case ScanCompletedRecord scanCompletedRecord:
					await ExecuteMarkCompletedAsync(transaction, scanCompletedRecord);
					break;
				case BeginRunRecord beginRunRecord:
					// Rare (once per run) and needs the auto-assigned RunNumber back, so it doesn't fit the
					// reusable-prepared-command pattern the rest of this switch uses - handled inline.
					int runNumber = await ExecuteBeginRunAsync(transaction, beginRunRecord);
					beginRunRecord.RunNumberResult.TrySetResult(runNumber);
					break;
				case FlushMarker flushMarker:
					(flushesToComplete ??= []).Add(flushMarker.Completed);
					break;
			}
		}

		await transaction.CommitAsync();

		if (flushesToComplete is not null)
		{
			foreach (TaskCompletionSource flush in flushesToComplete)
			{
				flush.TrySetResult();
			}
		}
	}

	private static SqliteCommand CreateUpsertDirectoryCommand(SqliteTransaction transaction)
	{
		SqliteCommand command = transaction.Connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
			"""
			INSERT INTO Directories (Url, ParentUrl, Name, Description, Finished, Error)
			VALUES ($url, $parentUrl, $name, $description, $finished, $error)
			ON CONFLICT(Url) DO UPDATE SET
				ParentUrl = excluded.ParentUrl,
				Name = excluded.Name,
				Description = excluded.Description,
				Finished = excluded.Finished,
				Error = excluded.Error;
			""";

		command.Parameters.Add("$url", SqliteType.Text);
		command.Parameters.Add("$parentUrl", SqliteType.Text);
		command.Parameters.Add("$name", SqliteType.Text);
		command.Parameters.Add("$description", SqliteType.Text);
		command.Parameters.Add("$finished", SqliteType.Integer);
		command.Parameters.Add("$error", SqliteType.Integer);

		return command;
	}

	private static void SetDirectoryParameters(SqliteCommand command, DirectoryRecord directoryRecord)
	{
		command.Parameters["$url"].Value = directoryRecord.Url;
		command.Parameters["$parentUrl"].Value = (object)directoryRecord.ParentUrl ?? DBNull.Value;
		command.Parameters["$name"].Value = (object)directoryRecord.Name ?? DBNull.Value;
		command.Parameters["$description"].Value = (object)directoryRecord.Description ?? DBNull.Value;
		command.Parameters["$finished"].Value = directoryRecord.Finished;
		command.Parameters["$error"].Value = directoryRecord.Error;
	}

	private static SqliteCommand CreateUpsertFileCommand(SqliteTransaction transaction)
	{
		SqliteCommand command = transaction.Connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
			"""
			INSERT INTO Files (Url, DirectoryUrl, FileName, FileSize, Description)
			VALUES ($url, $directoryUrl, $fileName, $fileSize, $description)
			ON CONFLICT(Url) DO UPDATE SET
				DirectoryUrl = excluded.DirectoryUrl,
				FileName = excluded.FileName,
				FileSize = excluded.FileSize,
				Description = excluded.Description;
			""";

		command.Parameters.Add("$url", SqliteType.Text);
		command.Parameters.Add("$directoryUrl", SqliteType.Text);
		command.Parameters.Add("$fileName", SqliteType.Text);
		command.Parameters.Add("$fileSize", SqliteType.Integer);
		command.Parameters.Add("$description", SqliteType.Text);

		return command;
	}

	private static void SetFileParameters(SqliteCommand command, FileRecord fileRecord)
	{
		command.Parameters["$url"].Value = fileRecord.Url;
		command.Parameters["$directoryUrl"].Value = fileRecord.DirectoryUrl;
		command.Parameters["$fileName"].Value = (object)fileRecord.FileName ?? DBNull.Value;
		command.Parameters["$fileSize"].Value = (object)fileRecord.FileSize ?? DBNull.Value;
		command.Parameters["$description"].Value = (object)fileRecord.Description ?? DBNull.Value;
	}

	private static SqliteCommand CreateUpdateFileSizeCommand(SqliteTransaction transaction)
	{
		SqliteCommand command = transaction.Connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "UPDATE Files SET FileSize = $fileSize WHERE Url = $url";

		command.Parameters.Add("$fileSize", SqliteType.Integer);
		command.Parameters.Add("$url", SqliteType.Text);

		return command;
	}

	private static void SetFileSizeParameters(SqliteCommand command, FileSizeRecord fileSizeRecord)
	{
		command.Parameters["$fileSize"].Value = (object)fileSizeRecord.FileSize ?? DBNull.Value;
		command.Parameters["$url"].Value = fileSizeRecord.Url;
	}

	private static SqliteCommand CreateUpsertSessionStatsCommand(SqliteTransaction transaction)
	{
		SqliteCommand command = transaction.Connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
			"""
			UPDATE ScanRuns SET
				LastUpdatedAtUtc = $updatedAtUtc,
				TotalHttpTraffic = $totalHttpTraffic,
				TotalHttpRequests = $totalHttpRequests,
				Errors = $errors,
				Skipped = $skipped,
				HttpStatusCodesJson = $httpStatusCodesJson
			WHERE RunNumber = $runNumber;
			""";

		command.Parameters.Add("$runNumber", SqliteType.Integer);
		command.Parameters.Add("$updatedAtUtc", SqliteType.Text);
		command.Parameters.Add("$totalHttpTraffic", SqliteType.Integer);
		command.Parameters.Add("$totalHttpRequests", SqliteType.Integer);
		command.Parameters.Add("$errors", SqliteType.Integer);
		command.Parameters.Add("$skipped", SqliteType.Integer);
		command.Parameters.Add("$httpStatusCodesJson", SqliteType.Text);

		return command;
	}

	private static void SetSessionStatsParameters(SqliteCommand command, SessionStatsRecord sessionStatsRecord)
	{
		command.Parameters["$runNumber"].Value = sessionStatsRecord.RunNumber;
		command.Parameters["$updatedAtUtc"].Value = sessionStatsRecord.UpdatedAtUtc.ToString("o");
		command.Parameters["$totalHttpTraffic"].Value = sessionStatsRecord.TotalHttpTraffic;
		command.Parameters["$totalHttpRequests"].Value = sessionStatsRecord.TotalHttpRequests;
		command.Parameters["$errors"].Value = sessionStatsRecord.Errors;
		command.Parameters["$skipped"].Value = sessionStatsRecord.Skipped;
		command.Parameters["$httpStatusCodesJson"].Value = sessionStatsRecord.HttpStatusCodesJson;
	}

	private static async Task<int> ExecuteBeginRunAsync(SqliteTransaction transaction, BeginRunRecord beginRunRecord)
	{
		string nowText = beginRunRecord.StartedAtUtc.ToString("o");

		if (beginRunRecord.IsFreshScan)
		{
			using SqliteCommand insertScanInfoCommand = transaction.Connection.CreateCommand();
			insertScanInfoCommand.Transaction = transaction;
			insertScanInfoCommand.CommandText =
				"""
				INSERT INTO ScanInfo (Id, RootUrl, FirstStartedAtUtc)
				VALUES (1, $rootUrl, $firstStartedAtUtc)
				ON CONFLICT(Id) DO NOTHING;
				""";
			insertScanInfoCommand.Parameters.AddWithValue("$rootUrl", beginRunRecord.RootUrl);
			insertScanInfoCommand.Parameters.AddWithValue("$firstStartedAtUtc", nowText);

			await insertScanInfoCommand.ExecuteNonQueryAsync();
		}

		using SqliteCommand insertRunCommand = transaction.Connection.CreateCommand();
		insertRunCommand.Transaction = transaction;
		insertRunCommand.CommandText =
			"""
			INSERT INTO ScanRuns (StartedAtUtc, LastUpdatedAtUtc, TotalHttpTraffic, TotalHttpRequests, Errors, Skipped, HttpStatusCodesJson)
			VALUES ($startedAtUtc, $startedAtUtc, $totalHttpTraffic, $totalHttpRequests, $errors, $skipped, $httpStatusCodesJson);
			SELECT last_insert_rowid();
			""";
		insertRunCommand.Parameters.AddWithValue("$startedAtUtc", nowText);
		insertRunCommand.Parameters.AddWithValue("$totalHttpTraffic", beginRunRecord.TotalHttpTraffic);
		insertRunCommand.Parameters.AddWithValue("$totalHttpRequests", beginRunRecord.TotalHttpRequests);
		insertRunCommand.Parameters.AddWithValue("$errors", beginRunRecord.Errors);
		insertRunCommand.Parameters.AddWithValue("$skipped", beginRunRecord.Skipped);
		insertRunCommand.Parameters.AddWithValue("$httpStatusCodesJson", beginRunRecord.HttpStatusCodesJson);

		return Convert.ToInt32(await insertRunCommand.ExecuteScalarAsync());
	}

	private static async Task ExecuteMarkCompletedAsync(SqliteTransaction transaction, ScanCompletedRecord scanCompletedRecord)
	{
		using SqliteCommand command = transaction.Connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "UPDATE ScanInfo SET CompletedAtUtc = $completedAtUtc WHERE Id = 1";
		command.Parameters.AddWithValue("$completedAtUtc", scanCompletedRecord.CompletedAtUtc.ToString("o"));

		await command.ExecuteNonQueryAsync();
	}

	public async ValueTask DisposeAsync()
	{
		_channel.Writer.TryComplete();

		await _writerTask;
		await _connection.DisposeAsync();

		// Microsoft.Data.Sqlite pools connections by default: disposing the SqliteConnection alone
		// does not release the underlying OS file handle, which would leave the file locked for
		// callers (e.g. deleting it right after) until the pool is cleared.
		SqliteConnection.ClearPool(_connection);
	}

	private interface IScanRecord;

	private sealed record DirectoryRecord(string Url, string ParentUrl, string Name, string Description, bool Finished, bool Error) : IScanRecord;

	private sealed record FileRecord(string Url, string DirectoryUrl, string FileName, long? FileSize, string Description) : IScanRecord;

	private sealed record FileSizeRecord(string Url, long? FileSize) : IScanRecord;

	private sealed record SessionStatsRecord(int RunNumber, DateTimeOffset UpdatedAtUtc, long TotalHttpTraffic, int TotalHttpRequests, int Errors, int Skipped, string HttpStatusCodesJson) : IScanRecord;

	private sealed record BeginRunRecord(bool IsFreshScan, string RootUrl, DateTimeOffset StartedAtUtc, long TotalHttpTraffic, int TotalHttpRequests, int Errors, int Skipped, string HttpStatusCodesJson, TaskCompletionSource<int> RunNumberResult) : IScanRecord;

	private sealed record ScanCompletedRecord(DateTimeOffset CompletedAtUtc) : IScanRecord;

	private sealed record FlushMarker(TaskCompletionSource Completed) : IScanRecord;
}
