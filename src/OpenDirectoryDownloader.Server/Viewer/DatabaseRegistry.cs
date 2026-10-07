using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Storage;
using System.Collections.Concurrent;

namespace OpenDirectoryDownloader.Server;

/// <summary>What one database found for a combined search</summary>
internal sealed record DatabaseSearchResult(DatabaseSession Session, ScanDatabase.SearchResult Result, string Error);

/// <summary>
/// The scan databases the viewer knows: explicitly given files, the *.sqlite files of given directories (which are
/// watched, so databases appearing, disappearing or growing show up), and uploaded ones. The summaries are counted
/// in the background and cached; <see cref="Version"/> changes whenever anything on the overview changed.
/// </summary>
internal sealed class DatabaseRegistry : IAsyncDisposable
{
	private readonly ConcurrentDictionary<string, DatabaseSession> _sessions = new();
	private readonly List<string> _directories = [];
	private readonly List<FileSystemWatcher> _watchers = [];
	private readonly SemaphoreSlim _refreshSlots = new(2);
	private readonly SemaphoreSlim _syncLock = new(1, 1);
	private readonly CancellationTokenSource _cts = new();
	private readonly ILogger _logger;
	private readonly TimeSpan _pollInterval;
	private readonly TimeSpan _minRefreshInterval;
	private readonly List<Task> _refreshTasks = [];
	private Timer _debounce;
	private Task _pollTask;
	private long _version;

	public DatabaseRegistry(ILogger logger, TimeSpan? pollInterval = null, TimeSpan? minRefreshInterval = null)
	{
		_logger = logger;
		_pollInterval = pollInterval ?? TimeSpan.FromSeconds(10);
		_minRefreshInterval = minRefreshInterval ?? TimeSpan.FromSeconds(20);
	}

	/// <summary>Changes whenever a database was added, removed or its summary changed</summary>
	public long Version => Interlocked.Read(ref _version);

	/// <summary>More than one database, or a directory: the overview is shown instead of a single database</summary>
	public bool IsMulti => _directories.Count > 0 || _sessions.Count > 1;

	public IReadOnlyList<DatabaseSession> Sessions => [.. _sessions.Values.OrderBy(session => session.Path, StringComparer.OrdinalIgnoreCase)];

	/// <summary>The database of the plain, non-overview viewer</summary>
	public DatabaseSession Default => IsMulti ? null : _sessions.Values.FirstOrDefault();

	public DatabaseSession Get(string id) => _sessions.TryGetValue(id, out DatabaseSession session) ? session : null;

	private void Changed() => Interlocked.Increment(ref _version);

	/// <summary>Adds the given files and directories and starts counting and watching. Paths that don't exist are reported through the returned messages.</summary>
	public async Task<List<string>> StartAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
	{
		List<string> problems = [];

		foreach (string path in paths)
		{
			string fullPath = Path.GetFullPath(path);

			if (Directory.Exists(fullPath))
			{
				_directories.Add(fullPath);
			}
			else if (File.Exists(fullPath))
			{
				AddFile(fullPath, isTemp: false, fromDirectory: false);
			}
			else
			{
				problems.Add($"'{path}' does not exist.");
			}
		}

		await SyncAsync(cancellationToken);

		foreach (string directory in _directories)
		{
			try
			{
				FileSystemWatcher watcher = new(directory, "*.sqlite") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
				watcher.Created += OnFileSystemEvent;
				watcher.Deleted += OnFileSystemEvent;
				watcher.Renamed += OnFileSystemEvent;
				watcher.Changed += OnFileSystemEvent;
				watcher.EnableRaisingEvents = true;
				_watchers.Add(watcher);
			}
			catch (Exception ex)
			{
				// The polling still finds changes
				_logger.LogWarning(ex, "Could not watch '{directory}'", directory);
			}
		}

		_debounce = new Timer(_ => _ = SyncSafeAsync(), null, Timeout.Infinite, Timeout.Infinite);
		_pollTask = Task.Run(PollAsync, CancellationToken.None);

		return problems;
	}

	private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
	{
		// A burst of events (a scan writing) becomes one pass
		_debounce?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
	}

	private async Task PollAsync()
	{
		using PeriodicTimer timer = new(_pollInterval);

		try
		{
			while (await timer.WaitForNextTickAsync(_cts.Token))
			{
				await SyncSafeAsync();
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task SyncSafeAsync()
	{
		try
		{
			await SyncAsync(_cts.Token);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Checking the databases for changes failed");
		}
	}

	/// <summary>Finds new and vanished databases in the directories and counts the ones that are new or changed.</summary>
	public async Task SyncAsync(CancellationToken cancellationToken)
	{
		await _syncLock.WaitAsync(cancellationToken);

		try
		{
			HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);

			foreach (string directory in _directories)
			{
				if (!Directory.Exists(directory))
				{
					continue;
				}

				foreach (string file in Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.TopDirectoryOnly))
				{
					found.Add(file);
					AddFile(file, isTemp: false, fromDirectory: true);
				}
			}

			foreach (DatabaseSession session in _sessions.Values.Where(session => session.FromDirectory && !found.Contains(session.Path)).ToList())
			{
				await RemoveAsync(session);
			}

			foreach (DatabaseSession session in _sessions.Values)
			{
				if (session.IsTemp)
				{
					continue;
				}

				bool never = session.LastRefreshedUtc == DateTime.MinValue;
				bool differs = session.Signature != FileSignature.Read(session.Path);

				// A scan that is still writing would otherwise be counted again every poll
				if (never || (differs && DateTime.UtcNow - session.LastRefreshedUtc >= _minRefreshInterval))
				{
					QueueRefresh(session);
				}
			}
		}
		finally
		{
			_syncLock.Release();
		}
	}

	private DatabaseSession AddFile(string path, bool isTemp, bool fromDirectory)
	{
		string id = DatabaseSession.CreateId(path);

		if (_sessions.TryGetValue(id, out DatabaseSession existing))
		{
			return existing;
		}

		DatabaseSession session = new(path, isTemp, fromDirectory, Changed);

		if (_sessions.TryAdd(id, session))
		{
			Changed();
			return session;
		}

		return _sessions[id];
	}

	private void QueueRefresh(DatabaseSession session)
	{
		Task task = Task.Run(async () =>
		{
			try
			{
				await _refreshSlots.WaitAsync(_cts.Token);

				try
				{
					await session.RefreshAsync(_cts.Token);
				}
				finally
				{
					_refreshSlots.Release();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Counting '{path}' failed", session.Path);
			}
		}, CancellationToken.None);

		lock (_refreshTasks)
		{
			_refreshTasks.RemoveAll(refreshTask => refreshTask.IsCompleted);
			_refreshTasks.Add(task);
		}
	}

	/// <summary>Waits until everything queued so far has been counted</summary>
	public async Task WaitForRefreshesAsync()
	{
		Task[] tasks;

		lock (_refreshTasks)
		{
			tasks = [.. _refreshTasks];
		}

		await Task.WhenAll(tasks);
	}

	/// <summary>Adds an uploaded database and counts it right away, so a failure can be reported. The file is deleted again when it fails or goes away.</summary>
	public async Task<DatabaseSession> AddUploadedAsync(string tempPath, CancellationToken cancellationToken)
	{
		bool wasMulti = IsMulti;
		List<DatabaseSession> previous = [.. _sessions.Values];

		DatabaseSession session = AddFile(tempPath, isTemp: true, fromDirectory: false);

		await session.RefreshAsync(cancellationToken);

		if (session.Summary.State is DatabaseState.Error or DatabaseState.Incompatible)
		{
			string error = session.Summary.Error;

			await RemoveAsync(session);

			throw new InvalidOperationException(error);
		}

		if (!wasMulti)
		{
			// The single-database viewer shows one database at a time, the new one replaces the old
			foreach (DatabaseSession old in previous)
			{
				await RemoveAsync(old);
			}
		}

		return session;
	}

	public async Task RemoveAsync(DatabaseSession session)
	{
		if (_sessions.TryRemove(session.Id, out _))
		{
			Changed();
		}

		await session.DisposeAsync();
	}

	/// <summary>The same search as for one database, in parallel over all of them. A database that fails only fails its own part.</summary>
	public async Task<List<DatabaseSearchResult>> SearchAllAsync(string term, int limitPerDatabase, CancellationToken cancellationToken)
	{
		using SemaphoreSlim slots = new(4);

		IEnumerable<Task<DatabaseSearchResult>> tasks = Sessions
			.Where(session => session.Summary.State is DatabaseState.Ready or DatabaseState.Loading)
			.Select(async session =>
			{
				await slots.WaitAsync(cancellationToken);

				try
				{
					return new DatabaseSearchResult(session, await session.QueryAsync(database => database.SearchAsync(term, limitPerDatabase), cancellationToken), null);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					return new DatabaseSearchResult(session, null, ex.Message);
				}
				finally
				{
					slots.Release();
				}
			});

		return [.. await Task.WhenAll(tasks)];
	}

	public async ValueTask DisposeAsync()
	{
		await _cts.CancelAsync();

		foreach (FileSystemWatcher watcher in _watchers)
		{
			watcher.Dispose();
		}

		_debounce?.Dispose();

		if (_pollTask is not null)
		{
			await _pollTask;
		}

		try
		{
			await WaitForRefreshesAsync();
		}
		catch (Exception)
		{
		}

		foreach (DatabaseSession session in _sessions.Values)
		{
			await session.DisposeAsync();
		}

		_sessions.Clear();
	}
}
