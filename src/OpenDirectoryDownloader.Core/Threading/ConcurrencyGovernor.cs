namespace OpenDirectoryDownloader.Threading;

/// <summary>
/// How many directories may be worked on at the same time. Starts at the configured number of threads; sites can
/// lower it for a reason (a Google Drive index wants 1, an FTP server allows fewer connections, the browser runs
/// one page at a time, the server is rate limiting). The effective limit is the lowest of all of them, so lifting
/// one reason (the browser is done) never undoes another (the Google Drive index still wants 1).
/// </summary>
public sealed class ConcurrencyGovernor
{
	private readonly Lock _lock = new();
	private readonly Dictionary<string, int> _caps = [];
	private readonly SemaphoreSlim _semaphore;
	private int _effective;

	/// <summary>Permits held by workers that must be swallowed when they come back, because the limit went down in the meantime.</summary>
	private int _debt;

	public ConcurrencyGovernor(int configured)
	{
		Configured = Math.Max(1, configured);
		_effective = Configured;
		_semaphore = new SemaphoreSlim(Configured, int.MaxValue);
	}

	public int Configured { get; }

	public int Effective
	{
		get
		{
			lock (_lock)
			{
				return _effective;
			}
		}
	}

	/// <summary>Lowers the limit for <paramref name="reason"/> until it is changed again, or removes it (null).</summary>
	public void SetCap(string reason, int? limit)
	{
		lock (_lock)
		{
			if (limit is null)
			{
				_caps.Remove(reason);
			}
			else
			{
				_caps[reason] = Math.Max(1, limit.Value);
			}

			Recompute();
		}
	}

	/// <summary>The current cap for <paramref name="reason"/>, if any.</summary>
	public int? GetCap(string reason)
	{
		lock (_lock)
		{
			return _caps.TryGetValue(reason, out int limit) ? limit : null;
		}
	}

	/// <summary>Lowers the limit for <paramref name="reason"/> while the returned object is not disposed.</summary>
	public IDisposable Cap(string reason, int limit)
	{
		SetCap(reason, limit);

		return new CapRelease(this, reason);
	}

	/// <summary>Waits for a slot. Dispose the lease when the work is done.</summary>
	public async ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken)
	{
		await _semaphore.WaitAsync(cancellationToken);

		return new Lease(this);
	}

	private void Release()
	{
		lock (_lock)
		{
			if (_debt > 0)
			{
				_debt--;
				return;
			}
		}

		_semaphore.Release();
	}

	private void Recompute()
	{
		int effective = Configured;

		foreach (int cap in _caps.Values)
		{
			effective = Math.Min(effective, cap);
		}

		int delta = effective - _effective;
		_effective = effective;

		if (delta > 0)
		{
			int paid = Math.Min(delta, _debt);
			_debt -= paid;
			delta -= paid;

			if (delta > 0)
			{
				_semaphore.Release(delta);
			}
		}
		else if (delta < 0)
		{
			int toTake = -delta;

			// Free permits can be taken away right now, the ones in use are swallowed when they come back
			while (toTake > 0 && _semaphore.Wait(0))
			{
				toTake--;
			}

			_debt += toTake;
		}
	}

	public readonly struct Lease(ConcurrencyGovernor governor) : IDisposable
	{
		public void Dispose() => governor?.Release();
	}

	private sealed class CapRelease(ConcurrencyGovernor governor, string reason) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				governor.SetCap(reason, null);
			}
		}
	}
}

/// <summary>The reasons the crawler lowers its concurrency.</summary>
public static class ConcurrencyReasons
{
	public const string GoogleDriveIndex = "Google Drive index";
	public const string GitHub = "GitHub";
	public const string Browser = "Browser";
	public const string RateLimited = "Rate limited";
	public const string FtpMaxConnections = "FTP maximum connections";
}
