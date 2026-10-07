using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Collections;
using System.Threading.Channels;

namespace OpenDirectoryDownloader.Storage;

/// <summary>
/// The scan database: every directory and file found, written as the scan goes and the source of every output
/// (URL list, JSON, statistics, the web viewer, --resume).
/// </summary>
/// <remarks>
/// <para>
/// Rows are keyed by integer ids the crawler assigns (<see cref="Models.WebDirectory.Id"/>), in the order things were
/// found, so listing a directory's children in their original order needs no sort, and a row costs a few bytes of
/// key instead of a full URL stored again in every index. Files keep only the part of their URL after their
/// directory's URL when they are below it.
/// </para>
/// <para>
/// All writes go through one bounded channel to a single writer task that applies them in large transactions,
/// so crawl workers never wait on disk, unless the disk falls far behind, which then slows the crawl down instead
/// of growing memory. Reads use a separate connection; in WAL mode they see everything committed.
/// </para>
/// </remarks>
public sealed partial class ScanDatabase : IAsyncDisposable
{
	/// <summary>Schema version in PRAGMA user_version (the databases of earlier releases have 0)</summary>
	private const int SchemaVersion = 1;

	private const int ChannelCapacity = 4096;

	private readonly string _readConnectionString;
	private readonly SqliteConnection _writeConnection;
	private readonly Channel<IScanRecord> _channel;
	private readonly Task _writerTask;
	private readonly ILogger _logger;

	private readonly SemaphoreSlim _readLock = new(1, 1);
	private SqliteConnection _readConnection;

	private long _lastDirectoryId;
	private long _lastFileId;

	/// <summary>The run (attempt) this process updates, set by BeginRunAsync</summary>
	private int _currentRunNumber;

	/// <summary>See <see cref="OpenReadOnlyAsync"/>: the -wal/-shm files this read-only instance created and should clean up.</summary>
	private readonly bool _ownsSidecarFiles;
	private readonly DateTime _mainFileLastWriteTimeUtcAtOpen;

	public string Path { get; }

	public bool IsReadOnly => _writeConnection is null;

	private ScanDatabase(string path, SqliteConnection writeConnection, string readConnectionString, ILogger logger, bool ownsSidecarFiles = false, long lastDirectoryId = 0, long lastFileId = 0)
	{
		Path = path;
		_lastDirectoryId = lastDirectoryId;
		_lastFileId = lastFileId;
		_writeConnection = writeConnection;
		_readConnectionString = readConnectionString;
		_logger = logger ?? NullLogger.Instance;
		_ownsSidecarFiles = ownsSidecarFiles;

		if (ownsSidecarFiles)
		{
			_mainFileLastWriteTimeUtcAtOpen = File.GetLastWriteTimeUtc(path);
		}

		if (writeConnection is not null)
		{
			_channel = Channel.CreateBounded<IScanRecord>(new BoundedChannelOptions(ChannelCapacity)
			{
				SingleReader = true,
				SingleWriter = false,
				FullMode = BoundedChannelFullMode.Wait
			});

			// From here on the write connection is the writer's alone (a connection is not thread safe)
			_writerTask = Task.Run(WriterLoopAsync);
		}
	}

	/// <summary>A new, empty database at <paramref name="path"/>, replacing any file that is there.</summary>
	public static Task<ScanDatabase> CreateAsync(string path, CancellationToken cancellationToken = default, ILogger logger = null) =>
		OpenAsync(path, deleteExisting: true, logger, cancellationToken);

	/// <summary>An existing database, to continue an interrupted scan (--resume). Creates an empty one when there is none.</summary>
	public static Task<ScanDatabase> OpenForResumeAsync(string path, CancellationToken cancellationToken = default, ILogger logger = null) =>
		OpenAsync(path, deleteExisting: false, logger, cancellationToken);

	private static async Task<ScanDatabase> OpenAsync(string path, bool deleteExisting, ILogger logger, CancellationToken cancellationToken)
	{
		string directory = System.IO.Path.GetDirectoryName(path);

		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		if (deleteExisting)
		{
			DeleteFiles(path);
		}

		SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);

		try
		{
			await connection.OpenAsync(cancellationToken);

			await ExecuteAsync(connection, "PRAGMA page_size = 8192;", cancellationToken);
			long existingVersion = (long)await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken);
			long tableCount = (long)await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';", cancellationToken);

			if (tableCount > 0 && existingVersion != SchemaVersion)
			{
				throw new InvalidOperationException($"'{path}' is a scan database from another version of OpenDirectoryDownloader (schema {existingVersion}, this version uses {SchemaVersion}) and cannot be resumed.");
			}

			// WAL before the schema, and the schema in one transaction: otherwise every statement is its own
			// transaction with the default synchronous = FULL, each one waiting for the disk
			await ExecuteAsync(connection,
				"""
				PRAGMA journal_mode = WAL;
				PRAGMA synchronous = NORMAL;
				PRAGMA cache_size = -65536;
				PRAGMA temp_store = MEMORY;
				PRAGMA wal_autocheckpoint = 10000;
				""", cancellationToken);
			await ExecuteAsync(connection, $"BEGIN;\n{Schema}\nCOMMIT;", cancellationToken);
		}
		catch
		{
			await connection.DisposeAsync();
			throw;
		}

		long lastDirectoryId;
		long lastFileId;

		try
		{
			lastDirectoryId = (long)await ScalarAsync(connection, "SELECT COALESCE(MAX(id), 0) FROM directory;", cancellationToken);
			lastFileId = (long)await ScalarAsync(connection, "SELECT COALESCE(MAX(id), 0) FROM file;", cancellationToken);
		}
		catch
		{
			await connection.DisposeAsync();
			throw;
		}

		return new ScanDatabase(path, connection, new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ConnectionString, logger, lastDirectoryId: lastDirectoryId, lastFileId: lastFileId);
	}

	/// <summary>
	/// An existing database, only for reading (the web viewer). Works on a database another process is still
	/// writing. Throws when the file does not exist or is not a scan database of this version.
	/// </summary>
	public static async Task<ScanDatabase> OpenReadOnlyAsync(string path, CancellationToken cancellationToken = default, ILogger logger = null)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Database file '{path}' not found.", path);
		}

		// Opening a WAL database creates its -wal/-shm files when they are not there; when this instance created
		// them it removes them again on dispose, when they were there some other (maybe still running) writer owns them
		bool ownsSidecarFiles = !File.Exists($"{path}-wal") && !File.Exists($"{path}-shm");

		string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString;

		await using (SqliteConnection connection = new(connectionString))
		{
			await connection.OpenAsync(cancellationToken);

			long matchingTables = (long)await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('directory', 'file', 'scan', 'run');", cancellationToken);
			long version = (long)await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken);

			if (matchingTables != 4 || version != SchemaVersion)
			{
				throw new InvalidOperationException(matchingTables == 0 && version == 0 ?
					$"'{path}' doesn't look like an OpenDirectoryDownloader scan database." :
					$"'{path}' is a scan database from another version of OpenDirectoryDownloader (schema {version}, this version reads {SchemaVersion}).");
			}
		}

		return new ScanDatabase(path, writeConnection: null, connectionString, logger, ownsSidecarFiles);
	}

	private const string Schema =
		"""
		PRAGMA user_version = 1;

		CREATE TABLE IF NOT EXISTS scan (
			id INTEGER PRIMARY KEY CHECK (id = 1),
			root_url TEXT NOT NULL,
			first_started_utc TEXT NOT NULL,
			completed_utc TEXT NULL,
			uploaded_urls_url TEXT NULL
		);

		CREATE TABLE IF NOT EXISTS run (
			run_number INTEGER PRIMARY KEY AUTOINCREMENT,
			started_utc TEXT NOT NULL,
			updated_utc TEXT NOT NULL,
			http_traffic INTEGER NOT NULL,
			http_requests INTEGER NOT NULL,
			errors INTEGER NOT NULL,
			skipped INTEGER NOT NULL,
			status_codes TEXT NOT NULL,
			speedtest_bytes INTEGER NULL,
			speedtest_ms INTEGER NULL,
			speedtest_max_bytes_per_second INTEGER NULL
		);

		-- id order is discovery order
		CREATE TABLE IF NOT EXISTS directory (
			id INTEGER PRIMARY KEY,
			parent_id INTEGER NULL,
			url TEXT NOT NULL,
			url_hash INTEGER NOT NULL,
			name TEXT NOT NULL,
			description TEXT NULL,
			finished INTEGER NOT NULL,
			error INTEGER NOT NULL,
			parser_id INTEGER NULL
		);

		-- The few dozen parser names, instead of one on every directory
		CREATE TABLE IF NOT EXISTS parser (
			id INTEGER PRIMARY KEY,
			name TEXT NOT NULL UNIQUE
		);

		CREATE INDEX IF NOT EXISTS directory_parent ON directory (parent_id);
		CREATE INDEX IF NOT EXISTS directory_url ON directory (url_hash);

		-- url is relative to the directory's url when relative = 1
		CREATE TABLE IF NOT EXISTS file (
			id INTEGER PRIMARY KEY,
			directory_id INTEGER NOT NULL,
			url TEXT NOT NULL,
			relative INTEGER NOT NULL,
			name TEXT NOT NULL,
			-- lower case, with the dot, empty for none: the statistics group by it
			extension TEXT NOT NULL,
			size INTEGER NULL,
			description TEXT NULL
		);

		CREATE INDEX IF NOT EXISTS file_directory ON file (directory_id);
		""";

	/// <summary>The 64-bit key a URL is looked up by (the full URL is compared too, so a collision only costs a row read).</summary>
	internal static long UrlHash(string url) => (long)(ulong)Hash128.Of(url);

	/// <summary>Waits until everything queued so far is committed. A no-op on a read-only instance.</summary>
	public async Task FlushAsync(CancellationToken cancellationToken = default)
	{
		if (IsReadOnly)
		{
			return;
		}

		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

		TaskCompletionSource flushCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

		await _channel.Writer.WriteAsync(new FlushMarker(flushCompleted), cancellationToken);
		await flushCompleted.Task.WaitAsync(cancellationToken);
	}

	private int _disposed;

	/// <summary>Writes what is queued, closes the connections. Safe to call more than once.</summary>
	public ValueTask DisposeAsync() => CloseAsync(checkpoint: true);

	/// <summary>Closes a database that is deleted right after: what is queued is still written, but not checkpointed into the database file.</summary>
	public ValueTask DiscardAsync() => CloseAsync(checkpoint: false);

	private async ValueTask CloseAsync(bool checkpoint)
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		if (!IsReadOnly)
		{
			_channel.Writer.TryComplete();

			await _writerTask;
		}

		// Leave a clean, self-contained file behind
		if (!IsReadOnly && checkpoint)
		{
			try
			{
				await ExecuteAsync(_writeConnection, "PRAGMA wal_checkpoint(TRUNCATE);", CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Could not checkpoint scan database '{path}'", Path);
			}
		}
		else if (!IsReadOnly)
		{
			// Closing the last connection checkpoints too
			SQLitePCL.raw.sqlite3_db_config(_writeConnection.Handle, SQLitePCL.raw.SQLITE_DBCONFIG_NO_CKPT_ON_CLOSE, 1, out _);
		}

		if (_readConnection is not null)
		{
			await _readConnection.DisposeAsync();
		}

		if (_writeConnection is not null)
		{
			await _writeConnection.DisposeAsync();
		}

		_readLock.Dispose();

		if (_ownsSidecarFiles)
		{
			DeleteOwnedSidecarFilesIfSafe();
		}
	}

	/// <summary>Deletes the database file and its -wal/-shm files.</summary>
	public static void DeleteFiles(string path)
	{
		foreach (string file in new[] { path, $"{path}-wal", $"{path}-shm", $"{path}-journal" })
		{
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	/// <summary>
	/// Removes the -wal/-shm files this read-only connection created by opening the database, unless the main file
	/// changed meanwhile (another process wrote to it and may still need its WAL).
	/// </summary>
	private void DeleteOwnedSidecarFilesIfSafe()
	{
		if (!File.Exists(Path) || File.GetLastWriteTimeUtc(Path) != _mainFileLastWriteTimeUtcAtOpen)
		{
			return;
		}

		foreach (string sidecarPath in new[] { $"{Path}-wal", $"{Path}-shm" })
		{
			try
			{
				if (File.Exists(sidecarPath))
				{
					File.Delete(sidecarPath);
				}
			}
			catch (IOException)
			{
				// Best effort: still locked by something, leave it
			}
		}
	}

	private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<object> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;

		return await command.ExecuteScalarAsync(cancellationToken);
	}
}
