namespace OpenDirectoryDownloader.Scanning;

/// <summary>
/// What the scan knows about rate limiting (HTTP 429 and the like): how many workers are waiting for it right now, and when
/// a directory was last fetched successfully. Together they tell a server that is just slowing us down (other requests still
/// work: keep trying) from one that has stopped answering altogether.
/// </summary>
public sealed class RateLimitTracker
{
	/// <summary>A success within this time means the server is still serving us</summary>
	public static readonly TimeSpan ProgressWindow = TimeSpan.FromMinutes(2);

	private int _waiting;
	private long _lastSuccessTicks;

	/// <summary>Workers waiting out a rate limit right now</summary>
	public int Waiting => Volatile.Read(ref _waiting);

	/// <summary>Counts the caller as waiting until the returned object is disposed</summary>
	public IDisposable BeginWait()
	{
		Interlocked.Increment(ref _waiting);

		return new Wait(this);
	}

	/// <summary>A directory was fetched without an error</summary>
	public void ReportSuccess() => Volatile.Write(ref _lastSuccessTicks, Environment.TickCount64);

	/// <summary>Whether something was fetched successfully within <paramref name="window"/> (<see cref="ProgressWindow"/> by default)</summary>
	public bool IsProgressing(TimeSpan? window = null)
	{
		long last = Volatile.Read(ref _lastSuccessTicks);

		return last != 0 && Environment.TickCount64 - last <= (window ?? ProgressWindow).TotalMilliseconds;
	}

	/// <summary>True when every one of the <paramref name="runningWorkers"/> workers is waiting for a rate limit</summary>
	public bool AllWaiting(int runningWorkers)
	{
		int waiting = Waiting;

		return waiting > 0 && waiting >= runningWorkers;
	}

	private sealed class Wait(RateLimitTracker tracker) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				Interlocked.Decrement(ref tracker._waiting);
			}
		}
	}
}
