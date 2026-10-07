using OpenDirectoryDownloader.Storage;
using System.Security.Cryptography;
using System.Text;

namespace OpenDirectoryDownloader.Server;

internal enum DatabaseState
{
	Loading,
	Ready,
	/// <summary>Not a scan database, or one from another version</summary>
	Incompatible,
	Error
}

/// <summary>What the overview shows about one database. Replaced as a whole, so readers never see a half-updated one.</summary>
internal sealed record DatabaseSummary(
	DatabaseState State,
	string Error,
	string RootUrl,
	DateTimeOffset? FirstStartedAtUtc,
	DateTimeOffset? CompletedAtUtc,
	DateTimeOffset? LastActivityAtUtc,
	long RunCount,
	IReadOnlyList<string> ParserTypes,
	/// <summary>False while the (full table scan) totals are still being counted</summary>
	bool StatsReady,
	long TotalFiles,
	long TotalDirectories,
	long TotalSize,
	long DatabaseSizeBytes,
	/// <summary>Average download speed of the --speedtest of the latest run that has one</summary>
	long? SpeedBytesPerSecond = null)
{
	public static DatabaseSummary Loading { get; } = new(DatabaseState.Loading, null, null, null, null, null, 0, [], false, 0, 0, 0, 0);
}

/// <summary>Cheap change detector for a database file: a running scan changes the WAL file or, at a checkpoint, the main file.</summary>
internal readonly record struct FileSignature(long Length, long LastWriteTicks, long WalLength)
{
	public static FileSignature Read(string path)
	{
		FileInfo main = new(path);
		FileInfo wal = new($"{path}-wal");

		return new FileSignature(main.Exists ? main.Length : -1, main.Exists ? main.LastWriteTimeUtc.Ticks : 0, wal.Exists ? wal.Length : 0);
	}
}

/// <summary>
/// One scan database known to the viewer. The ScanDatabase itself is only opened when something is browsed (a
/// directory of many databases shouldn't keep them all open); the summary is read with a short-lived connection.
/// All browsing access is serialized through one gate: Microsoft.Data.Sqlite connections aren't safe for
/// concurrent use, and the ASP.NET Core pipeline will happily run several requests at once. Read queries are
/// cheap, so coarse serialization isn't a real bottleneck. The ZIP endpoint only holds the gate for its brief
/// GetSubtreeAsync call.
/// </summary>
internal sealed class DatabaseSession : IAsyncDisposable
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Action _changed;
	private ScanDatabase _database;
	private DatabaseSummary _summary = DatabaseSummary.Loading;
	private int _refreshing;

	public DatabaseSession(string path, bool isTemp, bool fromDirectory, Action changed)
	{
		Path = path;
		IsTemp = isTemp;
		FromDirectory = fromDirectory;
		_changed = changed;
		Id = CreateId(path);
	}

	/// <summary>Stands in when a request names no (or an unknown) database, queries on it say that nothing is loaded</summary>
	public static DatabaseSession None { get; } = new(string.Empty, false, false, () => { });

	public string Id { get; }

	public string Path { get; }

	/// <summary>An uploaded copy, deleted again when the session ends</summary>
	public bool IsTemp { get; }

	/// <summary>Found by scanning a directory, so it goes away when the file does</summary>
	public bool FromDirectory { get; }

	public DatabaseSummary Summary => Volatile.Read(ref _summary);

	/// <summary>The file as it was when the summary was last (being) read</summary>
	public FileSignature Signature { get; private set; }

	public DateTime LastRefreshedUtc { get; private set; } = DateTime.MinValue;

	public static string CreateId(string path)
	{
		string normalized = OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;

		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..10].ToLowerInvariant();
	}

	private void Publish(DatabaseSummary summary)
	{
		Volatile.Write(ref _summary, summary);
		_changed();
	}

	/// <summary>Reads the summary again, first the cheap fields and then the totals. Does nothing when a refresh is already running.</summary>
	public async Task RefreshAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _refreshing, 1) != 0)
		{
			return;
		}

		ScanDatabase temporary = null;

		try
		{
			// Taken before reading, so a change while reading shows as a difference afterwards
			FileSignature signature = FileSignature.Read(Path);
			Signature = signature;
			DatabaseSummary previous = Summary;

			try
			{
				ScanDatabase database = Volatile.Read(ref _database);

				if (database is null)
				{
					if (signature.Length < 0)
					{
						throw new FileNotFoundException("The database file no longer exists.", Path);
					}

					temporary = await ScanDatabase.OpenReadOnlyAsync(Path, cancellationToken);
					database = temporary;
				}

				ScanDatabase.ScanInfoSnapshot info = await database.GetScanInfoAsync();

				if (info is null)
				{
					throw new InvalidOperationException("The database has no scan information.");
				}

				long runCount = await database.CountRunsAsync();
				List<string> parserTypes = await database.GetDistinctParserTypesAsync();
				ScanDatabase.SessionStatsSnapshot latestRun = runCount > 0 ? await database.GetLatestRunStatsAsync() : null;

				ScanDatabase.SpeedtestSnapshot speedtest = await database.GetLatestSpeedtestResultAsync();
				long? speed = speedtest is { DownloadedBytes: > 0, ElapsedMilliseconds: > 0 } ? speedtest.DownloadedBytes * 1000 / speedtest.ElapsedMilliseconds : null;

				DatabaseSummary cheap = new(
					DatabaseState.Ready, null, info.RootUrl, info.FirstStartedAtUtc, info.CompletedAtUtc, latestRun?.LastUpdatedAtUtc,
					runCount, parserTypes, previous.StatsReady, previous.TotalFiles, previous.TotalDirectories, previous.TotalSize, signature.Length, speed);

				Publish(cheap);

				ScanDatabase.OverallStats overall = await database.GetOverallStatsAsync();

				Publish(cheap with { StatsReady = true, TotalFiles = overall.TotalFiles, TotalDirectories = overall.TotalDirectories, TotalSize = overall.TotalSize });
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (InvalidOperationException ex)
			{
				Publish(DatabaseSummary.Loading with { State = DatabaseState.Incompatible, Error = ex.Message, DatabaseSizeBytes = Math.Max(0, signature.Length) });
			}
			catch (Exception ex)
			{
				Publish(DatabaseSummary.Loading with { State = DatabaseState.Error, Error = ex.Message, DatabaseSizeBytes = Math.Max(0, signature.Length) });
			}
		}
		finally
		{
			if (temporary is not null)
			{
				try
				{
					await temporary.DisposeAsync();
				}
				catch (IOException)
				{
					// Sidecar files still in use, they are cleaned up by whoever owns them
				}
			}

			LastRefreshedUtc = DateTime.UtcNow;
			Volatile.Write(ref _refreshing, 0);
		}
	}

	public async Task<T> QueryAsync<T>(Func<ScanDatabase, Task<T>> query, CancellationToken cancellationToken)
	{
		if (ReferenceEquals(this, None))
		{
			throw new InvalidOperationException("No database is loaded yet - drag a .sqlite file onto the page.");
		}

		await _gate.WaitAsync(cancellationToken);

		try
		{
			if (_database is null)
			{
				try
				{
					Volatile.Write(ref _database, await ScanDatabase.OpenReadOnlyAsync(Path, cancellationToken));
				}
				catch (FileNotFoundException)
				{
					throw new InvalidOperationException("The database file no longer exists.");
				}
			}

			return await query(_database);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Deletes an uploaded database's main file and its WAL/SHM sidecar files, if any (SQLite creates these for a WAL-mode database even when opened read-only).</summary>
	private static void DeleteTempDatabaseFiles(string path)
	{
		foreach (string candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
		{
			try
			{
				if (File.Exists(candidate))
				{
					File.Delete(candidate);
				}
			}
			catch (IOException)
			{
				// Left behind in the temp folder
			}
		}
	}

	/// <summary>Closing it is what cleans up the -wal/-shm sidecar files a read-only open creates (see ScanDatabase.OpenReadOnlyAsync).</summary>
	public async ValueTask DisposeAsync()
	{
		await _gate.WaitAsync();

		try
		{
			if (_database is not null)
			{
				await _database.DisposeAsync();
				_database = null;
			}
		}
		finally
		{
			_gate.Release();
		}

		if (IsTemp)
		{
			DeleteTempDatabaseFiles(Path);
		}
	}
}
