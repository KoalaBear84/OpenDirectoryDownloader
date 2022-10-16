namespace OpenDirectoryDownloader.Shared;

/// <summary>
/// Rate limiter
/// Use it to delay fast repeated requests so it won't hit the rate limits
/// </summary>
public class RateLimiter
{
	private readonly double TimeBetweenCalls;
	private DateTimeOffset LastRequest = DateTimeOffset.UtcNow;
	private object LockObject { get; set; } = new object();
	public int MaxRequestsPerTimeSpan { get; }
	public TimeSpan TimeSpan { get; }

	/// <summary>
	/// Creates a new instance of rate limiter
	/// </summary>
	/// <param name="maxRequestsPerTimeSpan">Maximum requests per time span</param>
	/// <param name="timeSpan">Time span to check against</param>
	/// <param name="margin">Default it uses 0.95 to only use 95% of the rates as an extra safety margin</param>
	public RateLimiter(int maxRequestsPerTimeSpan, TimeSpan timeSpan, double margin = 0.95d)
	{
		TimeBetweenCalls = timeSpan.TotalSeconds / maxRequestsPerTimeSpan / margin;
		MaxRequestsPerTimeSpan = maxRequestsPerTimeSpan;
		TimeSpan = timeSpan;
	}

	/// <summary>
	/// Wait for the next available time slot
	/// </summary>
	public async Task RateLimit()
	{
		do
		{
			if (DateTimeOffset.UtcNow < LastRequest.AddSeconds(TimeBetweenCalls))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(10));
			}
			else
			{
				lock (LockObject)
				{
					if (DateTimeOffset.UtcNow >= LastRequest.AddSeconds(TimeBetweenCalls))
					{
						LastRequest = DateTimeOffset.UtcNow;
						break;
					}
				}
			}
		} while (true);
	}

	/// <summary>
	/// Add extra delay in case of errors etc.
	/// </summary>
	/// <param name="timeSpan">TimeSpan to add</param>
	public void AddDelay(TimeSpan timeSpan)
	{
		LastRequest = LastRequest.Add(timeSpan);
	}
}
