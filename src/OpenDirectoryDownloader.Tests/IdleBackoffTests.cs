using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class IdleBackoffTests
{
	private static double[] Waits(ref IdleBackoff backoff, int count)
	{
		double[] waits = new double[count];

		for (int i = 0; i < count; i++)
		{
			waits[i] = backoff.NextWait().TotalMilliseconds;
		}

		return waits;
	}

	[Fact]
	public void StartsShort_DoublesWhileIdle_AndStopsAtTheMaximum()
	{
		IdleBackoff backoff = new();

		Assert.Equal([5d, 10, 20, 25, 25, 25], Waits(ref backoff, 6));
	}

	[Fact]
	public void FindingWork_StartsTheNextIdlePeriodShortAgain()
	{
		IdleBackoff backoff = new();

		Waits(ref backoff, 6);
		backoff.Reset();

		Assert.Equal([5d, 10, 20], Waits(ref backoff, 3));
	}

	[Fact]
	public void ResettingAFreshBackoff_ChangesNothing()
	{
		IdleBackoff backoff = new();
		backoff.Reset();

		Assert.Equal([5d, 10], Waits(ref backoff, 2));
	}

	[Fact]
	public void TheMaximumIsNoLongerThanTheFixedWaitItReplaces()
	{
		// The workers waited a fixed 100 ms before; the backoff must never wait longer than that
		Assert.True(IdleBackoff.Maximum <= TimeSpan.FromMilliseconds(100));
		Assert.True(IdleBackoff.Minimum < IdleBackoff.Maximum);
	}
}
