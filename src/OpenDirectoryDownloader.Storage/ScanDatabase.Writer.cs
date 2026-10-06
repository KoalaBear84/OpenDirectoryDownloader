using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Scanning;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenDirectoryDownloader.Storage;

public sealed partial class ScanDatabase
{
	/// <summary>Rows written per transaction at most; one transaction per batch is what makes SQLite fast here.</summary>
	private const int MaxRowsPerTransaction = 20_000;

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

		string url = webFile.Url;
		string directoryUrl = parentDirectory.Url;
		bool relative = directoryUrl is not null && url.Length > directoryUrl.Length && url.StartsWith(directoryUrl, StringComparison.Ordinal);

		return new FileRow(
			webFile.Id,
			parentDirectory.Id,
			relative ? url[directoryUrl.Length..] : url,
			relative,
			UrlHash(url),
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
		if (_channel.Writer.TryWrite(record))
		{
			return;
		}

		// Back pressure: only when the disk can't keep up
		_channel.Writer.WriteAsync(record).AsTask().GetAwaiter().GetResult();
	}

	private ValueTask WriteAsync(IScanRecord record, CancellationToken cancellationToken = default)
	{
		return _channel.Writer.TryWrite(record) ? ValueTask.CompletedTask : _channel.Writer.WriteAsync(record, cancellationToken);
	}

	private async Task WriterLoopAsync()
	{
		ChannelReader<IScanRecord> reader = _channel.Reader;

		try
		{
			using WriteCommands commands = new(_writeConnection);

			while (await reader.WaitToReadAsync())
			{
				List<TaskCompletionSource> flushesToComplete = null;

				using SqliteTransaction transaction = _writeConnection.BeginTransaction();
				commands.SetTransaction(transaction);

				int rows = 0;

				while (rows < MaxRowsPerTransaction && reader.TryRead(out IScanRecord record))
				{
					if (record is FlushMarker flushMarker)
					{
						(flushesToComplete ??= []).Add(flushMarker.Completed);
						continue;
					}

					rows += await ApplyAsync(commands, transaction, record);
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
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error writing to scan database '{path}'", Path);

			// Nothing will be written anymore: don't leave anyone waiting for a flush
			while (reader.TryRead(out IScanRecord record))
			{
				(record as FlushMarker)?.Completed.TrySetException(ex);
				(record as BeginRunRecord)?.RunNumberResult.TrySetException(ex);
			}
		}
	}

	private static async Task<int> ApplyAsync(WriteCommands commands, SqliteTransaction transaction, IScanRecord record)
	{
		switch (record)
		{
			case DirectoryRecord directoryRecord:
				await commands.UpsertDirectoryAsync(directoryRecord.Directory);
				return 1;
			case FileRecord fileRecord:
				await commands.UpsertFileAsync(fileRecord.File);
				return 1;
			case DirectoryListingRecord listingRecord:
				await commands.UpsertDirectoryAsync(listingRecord.Directory);

				foreach (FileRow file in listingRecord.Files)
				{
					await commands.UpsertFileAsync(file);
				}

				foreach (DirectoryRow subdirectory in listingRecord.Subdirectories)
				{
					await commands.UpsertDirectoryAsync(subdirectory);
				}

				return 1 + listingRecord.Files.Length + listingRecord.Subdirectories.Length;
			case DirectoryBecameFileRecord becameFileRecord:
				await ExecuteAsync(transaction, "DELETE FROM directory WHERE id = $id", ("$id", becameFileRecord.DirectoryId));
				await commands.UpsertFileAsync(becameFileRecord.File);
				return 2;
			case FileSizeRecord fileSizeRecord:
				await commands.UpdateFileSizeAsync(fileSizeRecord.Id, fileSizeRecord.FileSize);
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

	/// <summary>The writer's frequent statements, prepared once and reused for every row.</summary>
	private sealed class WriteCommands : IDisposable
	{
		private readonly SqliteCommand _upsertDirectory;
		private readonly SqliteCommand _upsertFile;
		private readonly SqliteCommand _updateFileSize;

		public WriteCommands(SqliteConnection connection)
		{
			_upsertDirectory = Create(connection,
				"""
				INSERT INTO directory (id, parent_id, url, url_hash, name, description, finished, error, parser)
				VALUES ($id, $parentId, $url, $urlHash, $name, $description, $finished, $error, $parser)
				ON CONFLICT(id) DO UPDATE SET
					parent_id = excluded.parent_id,
					url = excluded.url,
					url_hash = excluded.url_hash,
					name = excluded.name,
					description = excluded.description,
					finished = excluded.finished,
					error = excluded.error,
					parser = excluded.parser;
				""",
				("$id", SqliteType.Integer), ("$parentId", SqliteType.Integer), ("$url", SqliteType.Text), ("$urlHash", SqliteType.Integer),
				("$name", SqliteType.Text), ("$description", SqliteType.Text), ("$finished", SqliteType.Integer), ("$error", SqliteType.Integer), ("$parser", SqliteType.Text));

			_upsertFile = Create(connection,
				"""
				INSERT INTO file (id, directory_id, url, relative, url_hash, name, size, description)
				VALUES ($id, $directoryId, $url, $relative, $urlHash, $name, $size, $description)
				ON CONFLICT(id) DO UPDATE SET
					directory_id = excluded.directory_id,
					url = excluded.url,
					relative = excluded.relative,
					url_hash = excluded.url_hash,
					name = excluded.name,
					size = excluded.size,
					description = excluded.description;
				""",
				("$id", SqliteType.Integer), ("$directoryId", SqliteType.Integer), ("$url", SqliteType.Text), ("$relative", SqliteType.Integer),
				("$urlHash", SqliteType.Integer), ("$name", SqliteType.Text), ("$size", SqliteType.Integer), ("$description", SqliteType.Text));

			_updateFileSize = Create(connection, "UPDATE file SET size = $size WHERE id = $id;", ("$id", SqliteType.Integer), ("$size", SqliteType.Integer));
		}

		public void SetTransaction(SqliteTransaction transaction)
		{
			_upsertDirectory.Transaction = transaction;
			_upsertFile.Transaction = transaction;
			_updateFileSize.Transaction = transaction;
		}

		public Task<int> UpsertDirectoryAsync(DirectoryRow row)
		{
			SqliteParameterCollection parameters = _upsertDirectory.Parameters;
			parameters[0].Value = row.Id;
			parameters[1].Value = (object)row.ParentId ?? DBNull.Value;
			parameters[2].Value = row.Url;
			parameters[3].Value = UrlHash(row.Url);
			parameters[4].Value = row.Name;
			parameters[5].Value = (object)row.Description ?? DBNull.Value;
			parameters[6].Value = row.Finished;
			parameters[7].Value = row.Error;
			parameters[8].Value = (object)row.Parser ?? DBNull.Value;

			return _upsertDirectory.ExecuteNonQueryAsync();
		}

		public Task<int> UpsertFileAsync(FileRow row)
		{
			SqliteParameterCollection parameters = _upsertFile.Parameters;
			parameters[0].Value = row.Id;
			parameters[1].Value = row.DirectoryId;
			parameters[2].Value = row.Url;
			parameters[3].Value = row.Relative;
			parameters[4].Value = row.UrlHash;
			parameters[5].Value = row.Name;
			parameters[6].Value = (object)row.Size ?? DBNull.Value;
			parameters[7].Value = (object)row.Description ?? DBNull.Value;

			return _upsertFile.ExecuteNonQueryAsync();
		}

		public Task<int> UpdateFileSizeAsync(long id, long? size)
		{
			_updateFileSize.Parameters[0].Value = id;
			_updateFileSize.Parameters[1].Value = (object)size ?? DBNull.Value;

			return _updateFileSize.ExecuteNonQueryAsync();
		}

		public void Dispose()
		{
			_upsertDirectory.Dispose();
			_upsertFile.Dispose();
			_updateFileSize.Dispose();
		}

		private static SqliteCommand Create(SqliteConnection connection, string sql, params (string Name, SqliteType Type)[] parameters)
		{
			SqliteCommand command = connection.CreateCommand();
			command.CommandText = sql;

			foreach ((string name, SqliteType type) in parameters)
			{
				command.Parameters.Add(name, type);
			}

			return command;
		}
	}

	private interface IScanRecord;

	private sealed record DirectoryRow(long Id, long? ParentId, string Url, string Name, string Description, bool Finished, bool Error, string Parser);

	private sealed record FileRow(long Id, long DirectoryId, string Url, bool Relative, long UrlHash, string Name, long? Size, string Description);

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
