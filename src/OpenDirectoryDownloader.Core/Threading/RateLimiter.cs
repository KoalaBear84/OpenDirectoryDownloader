using System.Diagnostics;

namespace OpenDirectoryDownloader.Threading;

/// <summary>
/// Spaces out calls so they stay under a rate limit. Every caller reserves the next free time slot and sleeps
/// until it, instead of polling the clock every 10 ms.
/// </summary>
public sealed class RateLimiter
{
	private readonly long _intervalTicks;
	private readonly Lock _lock = new();
	private long _nextSlot;

	/// <param name="maxRequestsPerTimeSpan">Maximum requests per time span</param>
	/// <param name="timeSpan">Time span to check against</param>
	/// <param name="margin">Only use this part of the rate, as a safety margin</param>
	public RateLimiter(int maxRequestsPerTimeSpan, TimeSpan timeSpan, double margin = 0.95d)
	{
		MaxRequestsPerTimeSpan = maxRequestsPerTimeSpan;
		TimeSpan = timeSpan;
		_intervalTicks = (long)(timeSpan.TotalSeconds / maxRequestsPerTimeSpan / margin * Stopwatch.Frequency);
		_nextSlot = Stopwatch.GetTimestamp();
	}

	public int MaxRequestsPerTimeSpan { get; }

	public TimeSpan TimeSpan { get; }

	/// <summary>Waits for the next available time slot</summary>
	public Task RateLimit(CancellationToken cancellationToken = default)
	{
		long now = Stopwatch.GetTimestamp();
		long slot;

		lock (_lock)
		{
			slot = Math.Max(now, _nextSlot);
			_nextSlot = slot + _intervalTicks;
		}

		return slot <= now ? Task.CompletedTask : Task.Delay(Stopwatch.GetElapsedTime(now, slot), cancellationToken);
	}

	/// <summary>Pushes the next slot back, after errors etc.</summary>
	public void AddDelay(TimeSpan timeSpan)
	{
		lock (_lock)
		{
			_nextSlot = Math.Max(_nextSlot, Stopwatch.GetTimestamp()) + (long)(timeSpan.TotalSeconds * Stopwatch.Frequency);
		}
	}
}
