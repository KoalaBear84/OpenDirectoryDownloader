using OpenDirectoryDownloader.Threading;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>How many directories are worked on at once, and the sites that lower it.</summary>
public class ConcurrencyGovernorTests
{
	[Fact]
	public void TheLowestCapWins_AndLiftingOneLeavesTheOthers()
	{
		ConcurrencyGovernor governor = new(10);

		governor.SetCap(ConcurrencyReasons.GoogleDriveIndex, 1);

		using (governor.Cap(ConcurrencyReasons.Browser, 3))
		{
			Assert.Equal(1, governor.Effective);
		}

		// The browser is done, the Google Drive index still wants 1 (the old code went back to the configured threads)
		Assert.Equal(1, governor.Effective);

		governor.SetCap(ConcurrencyReasons.GoogleDriveIndex, null);
		Assert.Equal(10, governor.Effective);
	}

	[Fact]
	public async Task Leases_AreLimitedToTheEffectiveNumber()
	{
		ConcurrencyGovernor governor = new(3);
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		ConcurrencyGovernor.Lease first = await governor.AcquireAsync(cancellationToken);
		ConcurrencyGovernor.Lease second = await governor.AcquireAsync(cancellationToken);
		ConcurrencyGovernor.Lease third = await governor.AcquireAsync(cancellationToken);

		ValueTask<ConcurrencyGovernor.Lease> fourth = governor.AcquireAsync(cancellationToken);
		Assert.False(fourth.IsCompleted);

		first.Dispose();
		(await fourth).Dispose();
		second.Dispose();
		third.Dispose();
	}

	[Fact]
	public async Task LoweringWhileBusy_TakesEffectAsLeasesComeBack()
	{
		ConcurrencyGovernor governor = new(3);
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		ConcurrencyGovernor.Lease first = await governor.AcquireAsync(cancellationToken);
		ConcurrencyGovernor.Lease second = await governor.AcquireAsync(cancellationToken);

		governor.SetCap(ConcurrencyReasons.RateLimited, 1);

		// Two are busy, one may: nothing until both came back and one more
		ValueTask<ConcurrencyGovernor.Lease> next = governor.AcquireAsync(cancellationToken);
		Assert.False(next.IsCompleted);

		first.Dispose();
		Assert.False(next.IsCompleted);

		second.Dispose();
		(await next).Dispose();

		// Raising again gives the permits back
		governor.SetCap(ConcurrencyReasons.RateLimited, null);

		List<ConcurrencyGovernor.Lease> leases = [];

		for (int i = 0; i < 3; i++)
		{
			leases.Add(await governor.AcquireAsync(cancellationToken));
		}

		Assert.False(governor.AcquireAsync(cancellationToken).IsCompleted);

		leases.ForEach(lease => lease.Dispose());
	}
}
