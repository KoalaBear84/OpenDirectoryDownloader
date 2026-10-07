using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SQLitePCL;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Scanning;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenDirectoryDownloader.Storage;

public sealed partial class ScanDatabase
{
	/// <summary>Rows written per transaction at most; one transaction per batch is what makes SQLite fast here.</summary>
	private const int MaxRowsPerTransaction = 20_000;

	/// <summary>After this many records in a row could not be written, the database is given up on</summary>
	private const int MaxFailedRecordsInARow = 50;

	/// <summary>Stores <paramref name="webDirectory"/> (assigning its id the first time) or updates it. Its parent is stored first when it is not yet.</summary>
	public void MirrorDirectory(WebDirectory webDirectory)
	{
		Write(new DirectoryRecord(ToRow(webDirectory)));
	}

	/// <summary>Stores <paramref name="webFile"/>, found in <paramref name="parentDirectory"/> (assigning its id the first time), or updates it.</summary>
	public void MirrorFile(WebFile webFile, WebDirectory parentDirectory)
	{
		Write(new FileRecord(ToRow(webFile, parentDirectory)));
	}

	/// <summary>
	/// Stores a directory that was just listed in one go: its own row, all its files and the given new
	/// subdirectories (the ones that will be crawled). One channel item, one transaction, however big the listing.
	/// </summary>
	public ValueTask MirrorDirectoryListingAsync(WebDirectory webDirectory, IReadOnlyList<WebDirectory> newSubdirectories, CancellationToken cancellationToken = default)
	{
		DirectoryRow directoryRow = ToRow(webDirectory);

		FileRow[] files = new FileRow[webDirectory.Files.Count];

		for (int i = 0; i < files.Length; i++)
		{
			files[i] = ToRow(webDirectory.Files[i], webDirectory);
		}

		DirectoryRow[] subdirectories = new DirectoryRow[newSubdirectories.Count];

		for (int i = 0; i < subdirectories.Length; i++)
		{
			subdirectories[i] = ToRow(newSubdirectories[i]);
		}

		return WriteAsync(new DirectoryListingRecord(directoryRow, files, subdirectories), cancellationToken);
	}

	/// <summary>A queued subdirectory turned out to be a file: its directory row goes, a file row for it comes in its parent.</summary>
	public void MirrorDirectoryBecameFile(WebDirectory webDirectory, WebFile webFile, WebDirectory parentDirectory)
	{
		Write(new DirectoryBecameFileRecord(webDirectory.Id, ToRow(webFile, parentDirectory)));
	}

	public void MirrorFileSize(WebFile webFile)
	{
		if (webFile.Id == 0)
		{
			return;
		}

		Write(new FileSizeRecord(webFile.Id, webFile.FileSize));
	}

	/// <summary>Removes everything below a directory (files and subdirectories, recursively), before it is crawled again.</summary>
	public void ResetSubtree(long directoryId)
	{
		Write(new ResetSubtreeRecord(directoryId));
	}

	/// <summary>
	/// Records the start of an attempt at this scan: the first scan is run 1, every --resume adds one. A fresh scan
	/// also records the scan's identity (root URL, first start). The session's counters are the run's starting values.
	/// </summary>
	public async Task<int> BeginRunAsync(ScanSession session, string rootUrl, bool isFreshScan)
	{
		TaskCompletionSource<int> runNumberResult = new(TaskCreationOptions.RunContinuationsAsynchronously);

		await WriteAsync(new BeginRunRecord(isFreshScan, rootUrl, DateTimeOffset.UtcNow, Snapshot(session), runNumberResult));

		_currentRunNumber = await runNumberResult.Task;

		return _currentRunNumber;
	}

	/// <summary>Stores the session's counters on the current run, so --resume can continue them. A no-op before BeginRunAsync.</summary>
	public void MirrorSessionStats(ScanSession session)
	{
		if (_currentRunNumber == 0)
		{
			return;
		}

		Write(new SessionStatsRecord(_currentRunNumber, DateTimeOffset.UtcNow, Snapshot(session)));
	}

	/// <summary>Only on a full, successful finish (not on a pause).</summary>
	public void MarkCompleted(DateTimeOffset completedAtUtc)
	{
		Write(new ScanCompletedRecord(completedAtUtc));
	}

	/// <summary>Stores a --speedtest result on the current run; DownloadedBytes 0 means it failed. A no-op before BeginRunAsync.</summary>
	public void MirrorSpeedtestResult(SpeedtestResult speedtestResult)
	{
		if (_currentRunNumber == 0)
		{
			return;
		}

		Write(new SpeedtestResultRecord(_currentRunNumber, speedtestResult.DownloadedBytes, speedtestResult.ElapsedMilliseconds, speedtestResult.MaxBytesPerSecond));
	}

	/// <summary>The link of the uploaded URLs file, so a --resume with nothing left to do shows it instead of uploading again.</summary>
	public void MirrorUploadedUrlsUrl(string uploadedUrlsUrl)
	{
		Write(new UploadedUrlsUrlRecord(uploadedUrlsUrl));
	}

	private DirectoryRow ToRow(WebDirectory webDirectory)
	{
		if (webDirectory.Id == 0)
		{
			if (webDirectory.ParentDirectory is { Id: 0 } parent)
			{
				MirrorDirectory(parent);
			}

			webDirectory.Id = Interlocked.Increment(ref _lastDirectoryId);
		}

		return new DirectoryRow(
			webDirectory.Id,
			webDirectory.ParentDirectory?.Id,
			webDirectory.Url,
			webDirectory.Name ?? string.Empty,
			webDirectory.Description,
			webDirectory.Finished,
			webDirectory.Error,
			string.IsNullOrEmpty(webDirectory.Parser) ? null : webDirectory.Parser);
	}

	private FileRow ToRow(WebFile webFile, WebDirectory parentDirectory)
	{
		if (webFile.Id == 0)
		{
			webFile.Id = Interlocked.Increment(ref _lastFileId);
		}

		string directoryUrl = parentDirectory.Url;

		// A file attached to this directory already holds the relative part of its URL
		if (ReferenceEquals(webFile.ParentDirectory, parentDirectory) && webFile.TryGetRelativeUrl(out string relativeUrl))
		{
			return new FileRow(webFile.Id, parentDirectory.Id, relativeUrl, true, webFile.FileName ?? string.Empty, webFile.FileSize, webFile.Description);
		}

		string url = webFile.Url;
		bool relative = directoryUrl is not null && url.Length > directoryUrl.Length && url.StartsWith(directoryUrl, StringComparison.Ordinal);

		return new FileRow(
			webFile.Id,
			parentDirectory.Id,
			relative ? url[directoryUrl.Length..] : url,
			relative,
			webFile.FileName ?? string.Empty,
			webFile.FileSize,
			webFile.Description);
	}

	private static StatsSnapshot Snapshot(ScanSession session) => new(
		session.Counters.HttpTraffic,
		session.Counters.HttpRequests,
		session.Counters.Errors,
		session.Counters.Skipped,
		JsonSerializer.Serialize(new Dictionary<int, int>(session.HttpStatusCodes.Snapshot()), StorageJsonContext.Default.DictionaryInt32Int32));

	/// <summary>Queues a record without waiting, unless the writer is far behind (the channel is full).</summary>
	private void Write(IScanRecord record)
	{
		if (_channel.Writer.TryWrite(record) || IsClosed(record))
		{
			return;
		}

		// Back pressure: only when the disk can't keep up
		try
		{
			_channel.Writer.WriteAsync(record).AsTask().GetAwaiter().GetResult();
		}
		catch (ChannelClosedException)
		{
			IsClosed(record);
		}
	}

	private async ValueTask WriteAsync(IScanRecord record, CancellationToken cancellationToken = default)
	{
		if (_channel.Writer.TryWrite(record) || IsClosed(record))
		{
			return;
		}

		try
		{
			await _channel.Writer.WriteAsync(record, cancellationToken);
		}
		catch (ChannelClosedException)
		{
			IsClosed(record);
		}
	}

	/// <summary>Whether the database was closed already: what comes after that (a late statistics tick) is dropped.</summary>
	private bool IsClosed(IScanRecord record)
	{
		if (Volatile.Read(ref _disposed) == 0)
		{
			return false;
		}

		_logger.LogDebug("Scan database '{path}' is closed, {record} not written", Path, record.GetType().Name);
		(record as BeginRunRecord)?.RunNumberResult.TrySetException(new ObjectDisposedException(nameof(ScanDatabase)));

		return true;
	}

	/// <summary>
	/// The single writer: everything queued goes in transactions of up to <see cref="MaxRowsPerTransaction"/> rows.
	/// A scan never fails or waits on its database: a failed transaction is tried again record by record and a
	/// record that fails on its own is skipped. When nothing can be written anymore (disk full, file gone) the
	/// queue is still emptied, so nobody waits on it, and flushes fail so readers know the data is incomplete.
	/// </summary>
	private async Task WriterLoopAsync()
	{
		ChannelReader<IScanRecord> reader = _channel.Reader;
		List<IScanRecord> batch = [];
		List<TaskCompletionSource> flushes = [];
		Exception unwritable = null;
		int failedRecordsInARow = 0;
		WriteCommands commands = null;

		try
		{
			commands = new WriteCommands(_writeConnection);
		}
		catch (Exception ex)
		{
			unwritable = new IOException($"Scan database '{Path}' can't be written, the scan continues without it", ex);
			_logger.LogError(ex, "{message}", unwritable.Message);
		}

		using WriteCommands disposeCommands = commands;

		while (await reader.WaitToReadAsync())
		{
			batch.Clear();
			flushes.Clear();

			int rows = 0;

			while (rows < MaxRowsPerTransaction && reader.TryRead(out IScanRecord record))
			{
				if (record is FlushMarker flushMarker)
				{
					flushes.Add(flushMarker.Completed);
					continue;
				}

				batch.Add(record);
				rows += record is DirectoryListingRecord listing ? listing.Files.Length + listing.Subdirectories.Length + 1 : 1;
			}

			if (unwritable is null && batch.Count > 0 && !await TryApplyAsync(commands, batch))
			{
				// One by one, so one bad record doesn't lose the others
				foreach (IScanRecord record in batch)
				{
					if (await TryApplyAsync(commands, [record]))
					{
						failedRecordsInARow = 0;
						continue;
					}

					if (++failedRecordsInARow >= MaxFailedRecordsInARow)
					{
						unwritable = new IOException($"Scan database '{Path}' can't be written anymore, the scan continues without it");
						_logger.LogError("{message}", unwritable.Message);
						break;
					}
				}
			}

			if (unwritable is not null)
			{
				foreach (IScanRecord record in batch)
				{
					(record as BeginRunRecord)?.RunNumberResult.TrySetException(unwritable);
				}
			}

			foreach (TaskCompletionSource flush in flushes)
			{
				if (unwritable is null)
				{
					flush.TrySetResult();
				}
				else
				{
					flush.TrySetException(unwritable);
				}
			}
		}
	}

	/// <summary>Applies the records in one transaction, or nothing of them.</summary>
	private async Task<bool> TryApplyAsync(WriteCommands commands, List<IScanRecord> records)
	{
		SqliteTransaction transaction = null;

		try
		{
			transaction = _writeConnection.BeginTransaction();

			foreach (IScanRecord record in records)
			{
				await ApplyAsync(commands, transaction, record);
			}

			await transaction.CommitAsync();
			commands.Committed();

			return true;
		}
		catch (Exception ex)
		{
			try
			{
				if (transaction?.Connection is not null)
				{
					await transaction.RollbackAsync();
				}
			}
			catch (Exception rollbackException)
			{
				_logger.LogDebug(rollbackException, "Rollback failed on scan database '{path}'", Path);
			}

			commands.RolledBack();

			if (records.Count == 1)
			{
				_logger.LogError(ex, "Error writing {record} to scan database '{path}', skipped", records[0].GetType().Name, Path);
				(records[0] as BeginRunRecord)?.RunNumberResult.TrySetException(ex);
			}
			else
			{
				_logger.LogWarning(ex, "Error writing {count} records to scan database '{path}', trying them one by one", records.Count, Path);
			}

			return false;
		}
		finally
		{
			transaction?.Dispose();
		}
	}

	/// <remarks>The frequent statements run synchronously: this is the writer's own task, and SQLite's async methods are synchronous underneath anyway, with a task allocated per row.</remarks>
	private static async Task<int> ApplyAsync(WriteCommands commands, SqliteTransaction transaction, IScanRecord record)
	{
		switch (record)
		{
			case DirectoryRecord directoryRecord:
				commands.UpsertDirectory(directoryRecord.Directory);
				return 1;
			case FileRecord fileRecord:
				commands.UpsertFile(fileRecord.File);
				return 1;
			case DirectoryListingRecord listingRecord:
				commands.UpsertDirectory(listingRecord.Directory);

				commands.InsertFiles(listingRecord.Files);

				foreach (DirectoryRow subdirectory in listingRecord.Subdirectories)
				{
					commands.UpsertDirectory(subdirectory);
				}

				return 1 + listingRecord.Files.Length + listingRecord.Subdirectories.Length;
			case DirectoryBecameFileRecord becameFileRecord:
				await ExecuteAsync(transaction, "DELETE FROM directory WHERE id = $id", ("$id", becameFileRecord.DirectoryId));
				commands.UpsertFile(becameFileRecord.File);
				return 2;
			case FileSizeRecord fileSizeRecord:
				commands.UpdateFileSize(fileSizeRecord.Id, fileSizeRecord.FileSize);
				return 1;
			case ResetSubtreeRecord resetSubtreeRecord:
				await ExecuteAsync(transaction,
					"""
					WITH RECURSIVE subtree(id) AS (
						SELECT id FROM directory WHERE parent_id = $id
						UNION ALL
						SELECT d.id FROM directory d JOIN subtree s ON d.parent_id = s.id
					)
					DELETE FROM file WHERE directory_id = $id OR directory_id IN (SELECT id FROM subtree);

					WITH RECURSIVE subtree(id) AS (
						SELECT id FROM directory WHERE parent_id = $id
						UNION ALL
						SELECT d.id FROM directory d JOIN subtree s ON d.parent_id = s.id
					)
					DELETE FROM directory WHERE id IN (SELECT id FROM subtree);
					""", ("$id", resetSubtreeRecord.DirectoryId));
				return 1;
			case SessionStatsRecord sessionStatsRecord:
				await ExecuteAsync(transaction,
					"""
					UPDATE run SET
						updated_utc = $updatedUtc,
						http_traffic = $httpTraffic,
						http_requests = $httpRequests,
						errors = $errors,
						skipped = $skipped,
						status_codes = $statusCodes
					WHERE run_number = $runNumber;
					""",
					("$runNumber", sessionStatsRecord.RunNumber),
					("$updatedUtc", sessionStatsRecord.UpdatedAtUtc.ToString("o")),
					("$httpTraffic", sessionStatsRecord.Stats.HttpTraffic),
					("$httpRequests", sessionStatsRecord.Stats.HttpRequests),
					("$errors", sessionStatsRecord.Stats.Errors),
					("$skipped", sessionStatsRecord.Stats.Skipped),
					("$statusCodes", sessionStatsRecord.Stats.StatusCodesJson));
				return 1;
			case SpeedtestResultRecord speedtestRecord:
				await ExecuteAsync(transaction,
					"""
					UPDATE run SET
						speedtest_bytes = $bytes,
						speedtest_ms = $milliseconds,
						speedtest_max_bytes_per_second = $maxBytesPerSecond
					WHERE run_number = $runNumber;
					""",
					("$runNumber", speedtestRecord.RunNumber),
					("$bytes", speedtestRecord.DownloadedBytes),
					("$milliseconds", speedtestRecord.ElapsedMilliseconds),
					("$maxBytesPerSecond", speedtestRecord.MaxBytesPerSecond));
				return 1;
			case ScanCompletedRecord completedRecord:
				await ExecuteAsync(transaction, "UPDATE scan SET completed_utc = $completedUtc WHERE id = 1", ("$completedUtc", completedRecord.CompletedAtUtc.ToString("o")));
				return 1;
			case UploadedUrlsUrlRecord uploadedRecord:
				await ExecuteAsync(transaction, "UPDATE scan SET uploaded_urls_url = $url WHERE id = 1", ("$url", uploadedRecord.Url));
				return 1;
			case BeginRunRecord beginRunRecord:
				string startedText = beginRunRecord.StartedAtUtc.ToString("o");

				if (beginRunRecord.IsFreshScan)
				{
					await ExecuteAsync(transaction,
						"INSERT INTO scan (id, root_url, first_started_utc) VALUES (1, $rootUrl, $firstStartedUtc) ON CONFLICT(id) DO NOTHING;",
						("$rootUrl", beginRunRecord.RootUrl),
						("$firstStartedUtc", startedText));
				}

				using (SqliteCommand command = transaction.Connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText =
						"""
						INSERT INTO run (started_utc, updated_utc, http_traffic, http_requests, errors, skipped, status_codes)
						VALUES ($startedUtc, $startedUtc, $httpTraffic, $httpRequests, $errors, $skipped, $statusCodes);
						SELECT last_insert_rowid();
						""";
					command.Parameters.AddWithValue("$startedUtc", startedText);
					command.Parameters.AddWithValue("$httpTraffic", beginRunRecord.Stats.HttpTraffic);
					command.Parameters.AddWithValue("$httpRequests", beginRunRecord.Stats.HttpRequests);
					command.Parameters.AddWithValue("$errors", beginRunRecord.Stats.Errors);
					command.Parameters.AddWithValue("$skipped", beginRunRecord.Stats.Skipped);
					command.Parameters.AddWithValue("$statusCodes", beginRunRecord.Stats.StatusCodesJson);

					beginRunRecord.RunNumberResult.TrySetResult(Convert.ToInt32(await command.ExecuteScalarAsync()));
				}

				return 1;
			default:
				return 0;
		}
	}

	private static async Task ExecuteAsync(SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
	{
		using SqliteCommand command = transaction.Connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = sql;

		foreach ((string name, object value) in parameters)
		{
			command.Parameters.AddWithValue(name, value ?? DBNull.Value);
		}

		await command.ExecuteNonQueryAsync();
	}

	/// <summary>
	/// The writer's statements that run once per row, prepared once and bound by position straight on SQLite: through
	/// SqliteCommand every row costs a parameter lookup by name and a boxed value per column, most of a row's cost.
	/// They run in whatever transaction the connection has open.
	/// </summary>
	private sealed class WriteCommands(SqliteConnection connection) : IDisposable
	{
		private readonly RawStatement _upsertDirectory = new(connection.Handle,
			"""
			INSERT INTO directory (id, parent_id, url, url_hash, name, description, finished, error, parser_id)
			VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)
			ON CONFLICT(id) DO UPDATE SET
				parent_id = excluded.parent_id,
				url = excluded.url,
				url_hash = excluded.url_hash,
				name = excluded.name,
				description = excluded.description,
				finished = excluded.finished,
				error = excluded.error,
				parser_id = excluded.parser_id;
			""");

		private readonly RawStatement _insertParser = new(connection.Handle, "INSERT INTO parser (name) VALUES (?1);");

		/// <summary>The parser ids, the ones already in the database (a resumed scan) included</summary>
		private readonly Dictionary<string, long> _parserIds = ReadParserIds(connection);

		/// <summary>Parsers inserted in the open transaction: forgotten again when it is rolled back</summary>
		private readonly List<string> _uncommittedParsers = [];

		private const string InsertFileSql =
			"""
			INSERT INTO file (id, directory_id, url, relative, name, extension, size, description)
			VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
			""";

		private readonly RawStatement _upsertFile = new(connection.Handle,
			InsertFileSql +
			"""

			ON CONFLICT(id) DO UPDATE SET
				directory_id = excluded.directory_id,
				url = excluded.url,
				relative = excluded.relative,
				name = excluded.name,
				extension = excluded.extension,
				size = excluded.size,
				description = excluded.description;
			""");

		// The files of a new listing are new rows
		private readonly RawStatement _insertFile = new(connection.Handle, InsertFileSql);

		private readonly RawStatement _updateFileSize = new(connection.Handle, "UPDATE file SET size = ?2 WHERE id = ?1;");

		public void UpsertDirectory(DirectoryRow row)
		{
			_upsertDirectory.Bind(1, row.Id);
			_upsertDirectory.Bind(2, row.ParentId);
			_upsertDirectory.Bind(3, row.Url);
			_upsertDirectory.Bind(4, UrlHash(row.Url));
			_upsertDirectory.Bind(5, row.Name);
			_upsertDirectory.Bind(6, row.Description);
			_upsertDirectory.Bind(7, row.Finished ? 1 : 0);
			_upsertDirectory.Bind(8, row.Error ? 1 : 0);
			_upsertDirectory.Bind(9, ParserId(row.Parser));
			_upsertDirectory.Execute();
		}

		public void UpsertFile(FileRow row) => Execute(_upsertFile, row);

		public void InsertFile(FileRow row) => Execute(_insertFile, row);

		public void InsertFiles(FileRow[] rows)
		{
			foreach (FileRow row in rows)
			{
				Execute(_insertFile, row);
			}
		}

		private static void Execute(RawStatement statement, FileRow row)
		{
			statement.Bind(1, row.Id);
			statement.Bind(2, row.DirectoryId);
			statement.Bind(3, row.Url);
			statement.Bind(4, row.Relative ? 1 : 0);
			statement.Bind(5, row.Name);
			statement.Bind(6, Extension(row.Name));
			statement.Bind(7, row.Size);
			statement.Bind(8, row.Description);
			statement.Execute();
		}

		/// <summary>Lower case extension with the dot</summary>
		private static string Extension(string name)
		{
			ReadOnlySpan<char> extension = System.IO.Path.GetExtension(name.AsSpan());

			if (extension.IsEmpty)
			{
				return string.Empty;
			}

			foreach (char c in extension)
			{
				if (char.IsUpper(c))
				{
					return extension.ToString().ToLowerInvariant();
				}
			}

			return Extensions.GetOrAdd(extension);
		}

		public void UpdateFileSize(long id, long? size)
		{
			_updateFileSize.Bind(1, id);
			_updateFileSize.Bind(2, size);
			_updateFileSize.Execute();
		}

		private long? ParserId(string parser)
		{
			if (string.IsNullOrEmpty(parser))
			{
				return null;
			}

			if (!_parserIds.TryGetValue(parser, out long id))
			{
				_insertParser.Bind(1, parser);
				_insertParser.Execute();

				id = raw.sqlite3_last_insert_rowid(connection.Handle);
				_parserIds[parser] = id;
				_uncommittedParsers.Add(parser);
			}

			return id;
		}

		public void Committed() => _uncommittedParsers.Clear();

		public void RolledBack()
		{
			foreach (string parser in _uncommittedParsers)
			{
				_parserIds.Remove(parser);
			}

			_uncommittedParsers.Clear();
		}

		private static Dictionary<string, long> ReadParserIds(SqliteConnection connection)
		{
			Dictionary<string, long> parserIds = new(StringComparer.Ordinal);

			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT id, name FROM parser;";
			using SqliteDataReader reader = command.ExecuteReader();

			while (reader.Read())
			{
				parserIds[reader.GetString(1)] = reader.GetInt64(0);
			}

			return parserIds;
		}

		public void Dispose()
		{
			_insertParser.Dispose();
			_upsertDirectory.Dispose();
			_upsertFile.Dispose();
			_insertFile.Dispose();
			_updateFileSize.Dispose();
		}
	}

	/// <summary>A prepared SQLite statement with positional parameters (?1, ?2, ...).</summary>
	private sealed class RawStatement : IDisposable
	{
		private readonly sqlite3 _database;
		private readonly sqlite3_stmt _statement;

		public RawStatement(sqlite3 database, string sql)
		{
			_database = database;
			Check(raw.sqlite3_prepare_v2(database, sql, out _statement));
		}

		public void Bind(int index, long value) => Check(raw.sqlite3_bind_int64(_statement, index, value));

		public void Bind(int index, long? value) => Check(value is long number ? raw.sqlite3_bind_int64(_statement, index, number) : raw.sqlite3_bind_null(_statement, index));

		public void Bind(int index, string value) => Check(value is null ? raw.sqlite3_bind_null(_statement, index) : raw.sqlite3_bind_text(_statement, index, value));

		public void Execute()
		{
			int result = raw.sqlite3_step(_statement);
			raw.sqlite3_reset(_statement);

			if (result != raw.SQLITE_DONE)
			{
				Check(result);
			}
		}

		private void Check(int result)
		{
			if (result is not (raw.SQLITE_OK or raw.SQLITE_DONE or raw.SQLITE_ROW))
			{
				throw new SqliteException(raw.sqlite3_errmsg(_database).utf8_to_string(), result);
			}
		}

		public void Dispose() => _statement.Dispose();
	}

	/// <summary>Extension strings, shared instead of one per file row (a scan has a few hundred different ones)</summary>
	private static class Extensions
	{
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Known = new(StringComparer.Ordinal);
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> Lookup = Known.GetAlternateLookup<ReadOnlySpan<char>>();

		public static string GetOrAdd(ReadOnlySpan<char> extension)
		{
			if (Lookup.TryGetValue(extension, out string known))
			{
				return known;
			}

			string value = extension.ToString();

			// Bounded: a pathological scan can't fill it forever
			return Known.Count < 10_000 ? Known.GetOrAdd(value, value) : value;
		}
	}

	private interface IScanRecord;

	private sealed record DirectoryRow(long Id, long? ParentId, string Url, string Name, string Description, bool Finished, bool Error, string Parser);

	private sealed record FileRow(long Id, long DirectoryId, string Url, bool Relative, string Name, long? Size, string Description);

	private sealed record StatsSnapshot(long HttpTraffic, long HttpRequests, long Errors, long Skipped, string StatusCodesJson);

	private sealed record DirectoryRecord(DirectoryRow Directory) : IScanRecord;

	private sealed record FileRecord(FileRow File) : IScanRecord;

	private sealed record DirectoryListingRecord(DirectoryRow Directory, FileRow[] Files, DirectoryRow[] Subdirectories) : IScanRecord;

	private sealed record DirectoryBecameFileRecord(long DirectoryId, FileRow File) : IScanRecord;

	private sealed record FileSizeRecord(long Id, long? FileSize) : IScanRecord;

	private sealed record ResetSubtreeRecord(long DirectoryId) : IScanRecord;

	private sealed record SessionStatsRecord(int RunNumber, DateTimeOffset UpdatedAtUtc, StatsSnapshot Stats) : IScanRecord;

	private sealed record SpeedtestResultRecord(int RunNumber, long DownloadedBytes, long ElapsedMilliseconds, long MaxBytesPerSecond) : IScanRecord;

	private sealed record BeginRunRecord(bool IsFreshScan, string RootUrl, DateTimeOffset StartedAtUtc, StatsSnapshot Stats, TaskCompletionSource<int> RunNumberResult) : IScanRecord;

	private sealed record ScanCompletedRecord(DateTimeOffset CompletedAtUtc) : IScanRecord;

	private sealed record UploadedUrlsUrlRecord(string Url) : IScanRecord;

	private sealed record FlushMarker(TaskCompletionSource Completed) : IScanRecord;
}
