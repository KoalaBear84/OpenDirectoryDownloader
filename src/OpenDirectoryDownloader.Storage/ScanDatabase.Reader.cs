using Microsoft.Data.Sqlite;
using OpenDirectoryDownloader.Models;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OpenDirectoryDownloader.Storage;

public sealed partial class ScanDatabase
{
	// Per parent caches for the web viewer, valid as long as PRAGMA data_version reports no new commits
	private readonly Dictionary<long, Dictionary<string, SubtreeAggregate>> _subtreeAggregateCache = [];
	private readonly Dictionary<string, List<(string Url, string Name)>> _ancestorsCache = [];
	private long _cacheDataVersion = -1;

	/// <summary>
	/// Runs <paramref name="query"/> on the read connection, after everything queued so far is committed. One query
	/// at a time: the viewer and the console keys are not frequent, and a connection is not thread-safe.
	/// </summary>
	private async Task<T> ReadAsync<T>(Func<SqliteConnection, Task<T>> query, CancellationToken cancellationToken = default)
	{
		await FlushAsync(cancellationToken);
		await _readLock.WaitAsync(cancellationToken);

		try
		{
			return await query(await GetReadConnectionAsync(cancellationToken));
		}
		finally
		{
			_readLock.Release();
		}
	}

	private async Task<SqliteConnection> GetReadConnectionAsync(CancellationToken cancellationToken)
	{
		if (_readConnection is not null)
		{
			return _readConnection;
		}

		SqliteConnection connection = new(_readConnectionString);
		await connection.OpenAsync(cancellationToken);

		// Big CTEs and GROUP BYs stay in memory, mmap keeps reads off the page copy path
		using (SqliteCommand pragmaCommand = connection.CreateCommand())
		{
			pragmaCommand.CommandText = IsReadOnly ? "PRAGMA temp_store = MEMORY; PRAGMA mmap_size = 268435456;" : "PRAGMA query_only = 1; PRAGMA temp_store = MEMORY; PRAGMA mmap_size = 268435456;";
			await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		connection.CreateFunction("FileExtension", (string fileName) => System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant());

		_readConnection = connection;

		return connection;
	}

	private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
	{
		SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;

		foreach ((string name, object value) in parameters)
		{
			command.Parameters.AddWithValue(name, value ?? DBNull.Value);
		}

		return command;
	}

	private Task<long> CountAsync(string sql) => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection, sql);

		return (long)await command.ExecuteScalarAsync();
	});

	public Task<long> CountDirectoriesAsync() => CountAsync("SELECT COUNT(*) FROM directory");

	public Task<long> CountFilesAsync() => CountAsync("SELECT COUNT(*) FROM file");

	/// <summary>Directories marked Error. Used by --resume to ask whether to retry them.</summary>
	public Task<long> CountErroredDirectoriesAsync() => CountAsync("SELECT COUNT(*) FROM directory WHERE error = 1");

	/// <summary>How many attempts (runs) exist so far.</summary>
	public Task<long> CountRunsAsync() => CountAsync("SELECT COUNT(*) FROM run");

	/// <summary>The link of the URLs file an earlier run uploaded, or null.</summary>
	public Task<string> GetUploadedUrlsUrlAsync() => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection, "SELECT uploaded_urls_url FROM scan WHERE id = 1");

		return await command.ExecuteScalarAsync() as string;
	});

	/// <summary>The scan's identity (root URL, first start, completion), or null before the first run started.</summary>
	public Task<ScanInfoSnapshot> GetScanInfoAsync() => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection, "SELECT root_url, first_started_utc, completed_utc FROM scan WHERE id = 1");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new ScanInfoSnapshot(
			reader.GetString(0),
			DateTimeOffset.Parse(reader.GetString(1)),
			reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)));
	});

	/// <summary>The kinds of listing found across the scan (AList, Dufs, the generic table parser, ...), sorted.</summary>
	public Task<List<string>> GetDistinctParserTypesAsync() => ReadAsync(async connection =>
	{
		List<string> types = [];

		using SqliteCommand command = Command(connection, "SELECT DISTINCT parser FROM directory WHERE parser IS NOT NULL AND parser != '' ORDER BY parser");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			types.Add(reader.GetString(0));
		}

		return types;
	});

	/// <summary>The latest run's counters, or null when no run started yet.</summary>
	public Task<SessionStatsSnapshot> GetLatestRunStatsAsync() => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection,
			"""
			SELECT run_number, started_utc, updated_utc, http_traffic, http_requests, errors, skipped, status_codes
			FROM run
			ORDER BY run_number DESC
			LIMIT 1
			""");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		if (!await reader.ReadAsync())
		{
			return null;
		}

		Dictionary<int, int> httpStatusCodes = JsonSerializer.Deserialize(reader.GetString(7), StorageJsonContext.Default.DictionaryInt32Int32) ?? [];

		return new SessionStatsSnapshot(
			reader.GetInt32(0),
			DateTimeOffset.Parse(reader.GetString(1)),
			DateTimeOffset.Parse(reader.GetString(2)),
			reader.GetInt64(3),
			reader.GetInt64(4),
			reader.GetInt64(5),
			reader.GetInt64(6),
			httpStatusCodes);
	});

	/// <summary>The latest --speedtest result of any run, or null.</summary>
	public Task<SpeedtestSnapshot> GetLatestSpeedtestResultAsync() => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection,
			"""
			SELECT speedtest_bytes, speedtest_ms, speedtest_max_bytes_per_second
			FROM run
			WHERE speedtest_ms IS NOT NULL
			ORDER BY run_number DESC
			LIMIT 1
			""");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		return await reader.ReadAsync() ? new SpeedtestSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)) : null;
	});

	public async Task<long?> GetFileSizeAsync(string url) => (await GetFileInfoAsync(url))?.FileSize;

	/// <summary>The name and size of the file with this URL, or null. The name comes from the listing: for some sites (Google Drive) it is not in the URL.</summary>
	public Task<FileInfoSnapshot> GetFileInfoAsync(string url) => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection,
			"""
			SELECT f.name, f.size, CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END
			FROM file f
			JOIN directory d ON d.id = f.directory_id
			WHERE f.url_hash = $hash
			ORDER BY f.id
			""", ("$hash", UrlHash(url)));
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			if (reader.GetString(2) == url)
			{
				return new FileInfoSnapshot(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
			}
		}

		return null;
	});

	/// <summary>Totals over the whole database, in one query.</summary>
	public Task<OverallStats> GetOverallStatsAsync() => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection,
			"""
			SELECT
				(SELECT COUNT(*) FROM file),
				(SELECT COALESCE(SUM(size), 0) FROM file),
				(SELECT COUNT(*) FROM directory)
			""");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();
		await reader.ReadAsync();

		return new OverallStats(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
	});

	/// <summary>Count and size per (lower case) file extension, grouped by SQLite itself.</summary>
	public Task<Dictionary<string, ExtensionStats>> GetExtensionStatsAsync() => ReadAsync(async connection =>
	{
		Dictionary<string, ExtensionStats> stats = [];

		using SqliteCommand command = Command(connection, "SELECT FileExtension(name), COUNT(*), COALESCE(SUM(size), 0) FROM file GROUP BY FileExtension(name)");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			stats[reader.GetString(0)] = new ExtensionStats
			{
				Count = (int)reader.GetInt64(1),
				FileSize = reader.GetInt64(2)
			};
		}

		return stats;
	});

	/// <summary>The largest file, for --speedtest, or null when there are no files.</summary>
	public Task<MirroredFile> GetLargestFileAsync() => ReadAsync(async connection =>
	{
		using SqliteCommand command = Command(connection,
			"""
			SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END, f.name, f.size, f.description, f.id
			FROM file f
			JOIN directory d ON d.id = f.directory_id
			ORDER BY f.size DESC
			LIMIT 1
			""");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		return await reader.ReadAsync() ? ReadFile(reader) : null;
	});

	/// <summary>Every file URL, unordered, streamed (it is the biggest result there is).</summary>
	public async IAsyncEnumerable<string> ReadFileUrlsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await FlushAsync(cancellationToken);
		await _readLock.WaitAsync(cancellationToken);

		try
		{
			SqliteConnection connection = await GetReadConnectionAsync(cancellationToken);

			using SqliteCommand command = Command(connection, "SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END FROM file f JOIN directory d ON d.id = f.directory_id");
			using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

			while (await reader.ReadAsync(cancellationToken))
			{
				yield return reader.GetString(0);
			}
		}
		finally
		{
			_readLock.Release();
		}
	}

	/// <summary>
	/// Walks the tree from the directory with <paramref name="rootUrl"/> depth first, in discovery order (a
	/// directory's files, then each subdirectory in turn), for the JSON and aria2 outputs. One indexed query per
	/// directory; nothing but the current path is held in memory.
	/// </summary>
	public async Task WalkTreeAsync(string rootUrl, ITreeVisitor visitor, CancellationToken cancellationToken = default)
	{
		await FlushAsync(cancellationToken);
		await _readLock.WaitAsync(cancellationToken);

		try
		{
			SqliteConnection connection = await GetReadConnectionAsync(cancellationToken);

			MirroredDirectory root = await FindDirectoryAsync(connection, rootUrl);

			if (root is null)
			{
				await visitor.MissingRootAsync(rootUrl);
				return;
			}

			using SqliteCommand subdirectoriesCommand = Command(connection, "SELECT d.url, p.url, d.name, d.description, d.finished, d.error, d.id FROM directory d JOIN directory p ON p.id = d.parent_id WHERE d.parent_id = $id ORDER BY d.id", ("$id", 0L));
			using SqliteCommand filesCommand = Command(connection, "SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END, f.name, f.size, f.description, f.id FROM file f JOIN directory d ON d.id = f.directory_id WHERE f.directory_id = $id ORDER BY f.id", ("$id", 0L));

			async Task WalkAsync(MirroredDirectory directory)
			{
				cancellationToken.ThrowIfCancellationRequested();

				await visitor.EnterDirectoryAsync(directory);

				filesCommand.Parameters[0].Value = directory.Id;

				using (SqliteDataReader reader = await filesCommand.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						await visitor.FileAsync(directory, ReadFile(reader));
					}
				}

				// The children of this level are read before descending, the command is reused at every level
				List<MirroredDirectory> subdirectories = [];
				subdirectoriesCommand.Parameters[0].Value = directory.Id;

				using (SqliteDataReader reader = await subdirectoriesCommand.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						subdirectories.Add(ReadDirectory(reader));
					}
				}

				await visitor.BeginSubdirectoriesAsync(directory);

				foreach (MirroredDirectory subdirectory in subdirectories)
				{
					await WalkAsync(subdirectory);
				}

				await visitor.LeaveDirectoryAsync(directory);
			}

			await WalkAsync(root);
		}
		finally
		{
			_readLock.Release();
		}
	}

	/// <summary>Every directory with its parent id, in discovery order (for --resume).</summary>
	internal Task<List<StoredDirectory>> GetAllDirectoriesAsync() => ReadAsync(async connection =>
	{
		List<StoredDirectory> directories = [];

		using SqliteCommand command = Command(connection, "SELECT id, parent_id, url, name, description, finished, error, parser FROM directory ORDER BY id");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			directories.Add(new StoredDirectory(
				reader.GetInt64(0),
				reader.IsDBNull(1) ? null : reader.GetInt64(1),
				reader.GetString(2),
				reader.GetString(3),
				reader.IsDBNull(4) ? null : reader.GetString(4),
				reader.GetBoolean(5),
				reader.GetBoolean(6),
				reader.IsDBNull(7) ? null : reader.GetString(7)));
		}

		return directories;
	});

	/// <summary>File count, total size and unknown size count per directory id (for --resume).</summary>
	internal Task<Dictionary<long, FileAggregate>> GetFileAggregatesByDirectoryAsync() => ReadAsync(async connection =>
	{
		Dictionary<long, FileAggregate> aggregates = [];

		using SqliteCommand command = Command(connection, "SELECT directory_id, COUNT(*), COALESCE(SUM(size), 0), SUM(CASE WHEN size IS NULL THEN 1 ELSE 0 END) FROM file GROUP BY directory_id");
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			aggregates[reader.GetInt64(0)] = new FileAggregate(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
		}

		return aggregates;
	});

	/// <summary>The files of one directory, in discovery order (for --resume).</summary>
	internal Task<List<MirroredFile>> GetFilesByDirectoryIdAsync(long directoryId) => ReadAsync(async connection =>
	{
		List<MirroredFile> files = [];

		using SqliteCommand command = Command(connection, "SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END, f.name, f.size, f.description, f.id FROM file f JOIN directory d ON d.id = f.directory_id WHERE f.directory_id = $id ORDER BY f.id", ("$id", directoryId));
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			files.Add(ReadFile(reader));
		}

		return files;
	});

	public sealed record FileAggregate(long Count, long TotalSize, long NullSizeCount);

	internal sealed record StoredDirectory(long Id, long? ParentId, string Url, string Name, string Description, bool Finished, bool Error, string Parser);

	/// <summary>The directory with this URL (the first found, when a URL was stored twice), or null.</summary>
	public Task<MirroredDirectory> GetDirectoryAsync(string url) => ReadAsync(connection => FindDirectoryAsync(connection, url));

	private static async Task<MirroredDirectory> FindDirectoryAsync(SqliteConnection connection, string url)
	{
		using SqliteCommand command = Command(connection,
			"""
			SELECT d.url, p.url, d.name, d.description, d.finished, d.error, d.id
			FROM directory d
			LEFT JOIN directory p ON p.id = d.parent_id
			WHERE d.url_hash = $hash AND d.url = $url
			ORDER BY d.id
			LIMIT 1
			""", ("$hash", UrlHash(url)), ("$url", url));
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		return await reader.ReadAsync() ? ReadDirectory(reader) : null;
	}

	private static MirroredDirectory ReadDirectory(SqliteDataReader reader)
	{
		return new MirroredDirectory(
			reader.GetString(0),
			reader.IsDBNull(1) ? null : reader.GetString(1),
			reader.GetString(2),
			reader.IsDBNull(3) ? null : reader.GetString(3),
			reader.GetBoolean(4),
			reader.GetBoolean(5))
		{
			Id = reader.GetInt64(6)
		};
	}

	private static MirroredFile ReadFile(SqliteDataReader reader)
	{
		return new MirroredFile(
			reader.GetString(0),
			reader.GetString(1),
			reader.IsDBNull(2) ? null : reader.GetInt64(2),
			reader.IsDBNull(3) ? null : reader.GetString(3))
		{
			Id = reader.GetInt64(4)
		};
	}

	/// <summary>
	/// The ancestors of the directory with <paramref name="url"/>, from the root down to its parent, as (Url, Name),
	/// for the viewer's breadcrumbs: from the real parent links, so it works for URLs that are no paths at all.
	/// </summary>
	public Task<List<(string Url, string Name)>> GetAncestorsAsync(string url) => ReadAsync(async connection =>
	{
		await InvalidateCachesWhenChangedAsync(connection);

		if (_ancestorsCache.TryGetValue(url, out List<(string Url, string Name)> cached))
		{
			return cached;
		}

		List<(string Url, string Name)> ancestors = [];

		using (SqliteCommand command = Command(connection,
			"""
			WITH RECURSIVE ancestor(id, parent_id, url, name, depth) AS (
				SELECT p.id, p.parent_id, p.url, p.name, 1
				FROM directory d
				JOIN directory p ON p.id = d.parent_id
				WHERE d.id = (SELECT id FROM directory WHERE url_hash = $hash AND url = $url ORDER BY id LIMIT 1)
				UNION ALL
				SELECT p.id, p.parent_id, p.url, p.name, a.depth + 1
				FROM ancestor a
				JOIN directory p ON p.id = a.parent_id
				WHERE a.depth < 1000
			)
			SELECT url, name FROM ancestor ORDER BY depth DESC
			""", ("$hash", UrlHash(url)), ("$url", url)))
		{
			using SqliteDataReader reader = await command.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				ancestors.Add((reader.GetString(0), reader.GetString(1)));
			}
		}

		_ancestorsCache[url] = ancestors;

		return ancestors;
	});

	/// <summary>The subdirectories of the directory with <paramref name="parentUrl"/>, in discovery order.</summary>
	public Task<List<MirroredDirectory>> GetSubdirectoriesAsync(string parentUrl) => ReadAsync(async connection =>
	{
		List<MirroredDirectory> directories = [];
		MirroredDirectory parent = await FindDirectoryAsync(connection, parentUrl);

		if (parent is null)
		{
			return directories;
		}

		using SqliteCommand command = Command(connection, "SELECT d.url, p.url, d.name, d.description, d.finished, d.error, d.id FROM directory d JOIN directory p ON p.id = d.parent_id WHERE d.parent_id = $id ORDER BY d.id", ("$id", parent.Id));
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			directories.Add(ReadDirectory(reader));
		}

		return directories;
	});

	/// <summary>The files of the directory with <paramref name="directoryUrl"/>, in discovery order.</summary>
	public Task<List<MirroredFile>> GetFilesAsync(string directoryUrl) => ReadAsync(async connection =>
	{
		List<MirroredFile> files = [];
		MirroredDirectory directory = await FindDirectoryAsync(connection, directoryUrl);

		if (directory is null)
		{
			return files;
		}

		using SqliteCommand command = Command(connection, "SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END, f.name, f.size, f.description, f.id FROM file f JOIN directory d ON d.id = f.directory_id WHERE f.directory_id = $id ORDER BY f.id", ("$id", directory.Id));
		using SqliteDataReader reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			files.Add(ReadFile(reader));
		}

		return files;
	});

	/// <summary>
	/// For each subdirectory of <paramref name="parentUrl"/>, the totals of its whole subtree (itself included), keyed
	/// by its URL, for the viewer's size bars. Cached until the database changes.
	/// </summary>
	public Task<Dictionary<string, SubtreeAggregate>> GetChildSubtreeAggregatesAsync(string parentUrl) => ReadAsync(async connection =>
	{
		await InvalidateCachesWhenChangedAsync(connection);

		MirroredDirectory parent = await FindDirectoryAsync(connection, parentUrl);

		if (parent is null)
		{
			return [];
		}

		if (_subtreeAggregateCache.TryGetValue(parent.Id, out Dictionary<string, SubtreeAggregate> cached))
		{
			return cached;
		}

		Dictionary<string, SubtreeAggregate> aggregates = [];

		using (SqliteCommand command = Command(connection,
			"""
			WITH RECURSIVE subtree(id, anchor) AS (
				SELECT id, id FROM directory WHERE parent_id = $id
				UNION ALL
				SELECT d.id, s.anchor
				FROM directory d
				JOIN subtree s ON d.parent_id = s.id
			),
			per_directory(id, anchor, file_count, total_size, unknown_size_count) AS (
				SELECT s.id, s.anchor, COUNT(f.id), COALESCE(SUM(f.size), 0), SUM(CASE WHEN f.id IS NOT NULL AND f.size IS NULL THEN 1 ELSE 0 END)
				FROM subtree s
				LEFT JOIN file f ON f.directory_id = s.id
				GROUP BY s.id
			)
			SELECT a.url, COUNT(*), SUM(p.file_count), SUM(p.total_size), SUM(p.unknown_size_count)
			FROM per_directory p
			JOIN directory a ON a.id = p.anchor
			GROUP BY p.anchor
			""", ("$id", parent.Id)))
		{
			using SqliteDataReader reader = await command.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				aggregates[reader.GetString(0)] = new SubtreeAggregate(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.IsDBNull(4) ? 0 : reader.GetInt64(4));
			}
		}

		_subtreeAggregateCache[parent.Id] = aggregates;

		return aggregates;
	});

	/// <summary>Clears the viewer caches when another connection committed something since they were filled.</summary>
	private async Task InvalidateCachesWhenChangedAsync(SqliteConnection connection)
	{
		using SqliteCommand command = Command(connection, "PRAGMA data_version");
		long dataVersion = (long)await command.ExecuteScalarAsync();

		if (dataVersion != _cacheDataVersion)
		{
			_subtreeAggregateCache.Clear();
			_ancestorsCache.Clear();
			_cacheDataVersion = dataVersion;
		}
	}

	/// <summary>
	/// Every directory below <paramref name="rootUrl"/> (not itself) and every file in its subtree (its own too), for
	/// the viewer's ZIP download, which rebuilds relative paths from the parent links.
	/// </summary>
	public Task<SubtreeResult> GetSubtreeAsync(string rootUrl) => ReadAsync(async connection =>
	{
		List<MirroredDirectory> directories = [];
		List<SubtreeFile> files = [];

		MirroredDirectory root = await FindDirectoryAsync(connection, rootUrl);

		if (root is null)
		{
			return new SubtreeResult(directories, files);
		}

		const string subtree =
			"""
			WITH RECURSIVE subtree(id) AS (
				SELECT $id
				UNION ALL
				SELECT d.id FROM directory d JOIN subtree s ON d.parent_id = s.id
			)
			""";

		using (SqliteCommand command = Command(connection,
			$"""
			{subtree}
			SELECT d.url, p.url, d.name, d.description, d.finished, d.error, d.id
			FROM directory d
			JOIN directory p ON p.id = d.parent_id
			WHERE d.id IN (SELECT id FROM subtree) AND d.id != $id
			ORDER BY d.id
			""", ("$id", root.Id)))
		{
			using SqliteDataReader reader = await command.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				directories.Add(ReadDirectory(reader));
			}
		}

		using (SqliteCommand command = Command(connection,
			$"""
			{subtree}
			SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END, d.url, f.name, f.size, f.description
			FROM file f
			JOIN directory d ON d.id = f.directory_id
			WHERE f.directory_id IN (SELECT id FROM subtree)
			ORDER BY f.id
			""", ("$id", root.Id)))
		{
			using SqliteDataReader reader = await command.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				files.Add(new SubtreeFile(
					reader.GetString(0),
					reader.GetString(1),
					reader.GetString(2),
					reader.IsDBNull(3) ? null : reader.GetInt64(3),
					reader.IsDBNull(4) ? null : reader.GetString(4)));
			}
		}

		return new SubtreeResult(directories, files);
	});

	/// <summary>
	/// Files and directories anywhere in the scan whose name contains <paramref name="term"/> (case-insensitive for
	/// ASCII), at most <paramref name="limit"/> of each. '%' and '_' in the term match literally.
	/// </summary>
	public Task<SearchResult> SearchAsync(string term, int limit) => ReadAsync(async connection =>
	{
		string pattern = $"%{EscapeLikePattern(term)}%";

		List<SearchFileMatch> files = [];
		List<SearchDirectoryMatch> directories = [];

		using (SqliteCommand command = Command(connection,
			"""
			SELECT CASE WHEN f.relative = 1 THEN d.url || f.url ELSE f.url END, d.url, f.name, f.size, f.description
			FROM file f
			JOIN directory d ON d.id = f.directory_id
			WHERE f.name LIKE $pattern ESCAPE '\'
			ORDER BY f.name
			LIMIT $limit
			""", ("$pattern", pattern), ("$limit", limit + 1)))
		{
			using SqliteDataReader reader = await command.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				files.Add(new SearchFileMatch(
					reader.GetString(0),
					reader.GetString(1),
					reader.GetString(2),
					reader.IsDBNull(3) ? null : reader.GetInt64(3),
					reader.IsDBNull(4) ? null : reader.GetString(4)));
			}
		}

		using (SqliteCommand command = Command(connection,
			"""
			SELECT d.url, p.url, d.name, d.finished, d.error
			FROM directory d
			LEFT JOIN directory p ON p.id = d.parent_id
			WHERE d.name LIKE $pattern ESCAPE '\'
			ORDER BY d.name
			LIMIT $limit
			""", ("$pattern", pattern), ("$limit", limit + 1)))
		{
			using SqliteDataReader reader = await command.ExecuteReaderAsync();

			while (await reader.ReadAsync())
			{
				directories.Add(new SearchDirectoryMatch(
					reader.GetString(0),
					reader.IsDBNull(1) ? null : reader.GetString(1),
					reader.GetString(2),
					reader.GetBoolean(3),
					reader.GetBoolean(4)));
			}
		}

		bool truncated = files.Count > limit || directories.Count > limit;

		if (files.Count > limit)
		{
			files.RemoveRange(limit, files.Count - limit);
		}

		if (directories.Count > limit)
		{
			directories.RemoveRange(limit, directories.Count - limit);
		}

		return new SearchResult(files, directories, truncated);
	});

	private static string EscapeLikePattern(string term) =>
		term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

	public sealed record ScanInfoSnapshot(string RootUrl, DateTimeOffset FirstStartedAtUtc, DateTimeOffset? CompletedAtUtc);

	public sealed record SessionStatsSnapshot(int RunNumber, DateTimeOffset StartedAtUtc, DateTimeOffset LastUpdatedAtUtc, long TotalHttpTraffic, long TotalHttpRequests, long Errors, long Skipped, Dictionary<int, int> HttpStatusCodes);

	public sealed record SpeedtestSnapshot(long DownloadedBytes, long ElapsedMilliseconds, long MaxBytesPerSecond);

	public sealed record FileInfoSnapshot(string FileName, long? FileSize);

	public sealed record OverallStats(long TotalFiles, long TotalSize, long TotalDirectories);

	public sealed record SubtreeAggregate(long DirectoryCount, long FileCount, long TotalSize, long UnknownSizeCount);

	public sealed record SearchResult(List<SearchFileMatch> Files, List<SearchDirectoryMatch> Directories, bool Truncated);

	public sealed record SearchFileMatch(string Url, string DirectoryUrl, string FileName, long? FileSize, string Description);

	public sealed record SearchDirectoryMatch(string Url, string ParentUrl, string Name, bool Finished, bool Error);

	public sealed record SubtreeResult(List<MirroredDirectory> Directories, List<SubtreeFile> Files);

	public sealed record SubtreeFile(string Url, string DirectoryUrl, string FileName, long? FileSize, string Description);

	public sealed record MirroredDirectory(string Url, string ParentUrl, string Name, string Description, bool Finished, bool Error)
	{
		public long Id { get; init; }
	}

	public sealed record MirroredFile(string Url, string FileName, long? FileSize, string Description)
	{
		public long Id { get; init; }
	}

	/// <summary>What <see cref="WalkTreeAsync"/> calls, in depth first order.</summary>
	public interface ITreeVisitor
	{
		Task MissingRootAsync(string rootUrl) => Task.CompletedTask;

		Task EnterDirectoryAsync(MirroredDirectory directory);

		Task FileAsync(MirroredDirectory directory, MirroredFile file);

		/// <summary>After the directory's files, before its first subdirectory</summary>
		Task BeginSubdirectoriesAsync(MirroredDirectory directory) => Task.CompletedTask;

		Task LeaveDirectoryAsync(MirroredDirectory directory);
	}
}
