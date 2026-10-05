using OpenDirectoryDownloader.Shared;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class ProcessedUrlSetTests
{
	[Fact]
	public void AddContainsRemove_BehaveLikeASetOfUrls()
	{
		ProcessedUrlSet set = new();

		Assert.False(set.Contains("http://localhost/a/"));
		Assert.True(set.Add("http://localhost/a/"));
		Assert.False(set.Add("http://localhost/a/"));
		Assert.True(set.Contains("http://localhost/a/"));
		Assert.False(set.Contains("http://localhost/A/"));
		Assert.Equal(1, set.Count);

		Assert.True(set.Remove("http://localhost/a/"));
		Assert.False(set.Remove("http://localhost/a/"));
		Assert.False(set.Contains("http://localhost/a/"));
		Assert.Equal(0, set.Count);
	}

	[Fact]
	public async Task ConcurrentAdds_OfTheSameUrl_OnlyOneWins()
	{
		ProcessedUrlSet set = new();
		int winners = 0;

		await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
		{
			if (set.Add("http://localhost/shared/"))
			{
				Interlocked.Increment(ref winners);
			}
		})));

		Assert.Equal(1, winners);
	}

	[Fact]
	public void Hash128_ManyDifferentUrls_DoNotCollide()
	{
		HashSet<UInt128> hashes = [];
		const int count = 200_000;

		for (int i = 0; i < count; i++)
		{
			// Realistic: long shared prefix, the difference only at the end
			hashes.Add(Hash128.Of($"http://files.example.test:8086/some/long/shared/prefix/dir-{i / 1000:D4}/sub-{i % 1000:D4}/"));
		}

		Assert.Equal(count, hashes.Count);
	}

	[Fact]
	public void Hash128_DependsOnTheWholeInput_NotJustTheEnd()
	{
		// A weak second lane (even multiplier) would forget characters more than ~64 positions back in both halves' low bits
		string tail = new('x', 200);
		UInt128 first = Hash128.Of("a" + tail);
		UInt128 second = Hash128.Of("b" + tail);

		Assert.NotEqual(first, second);
		Assert.NotEqual((ulong)(first >> 64), (ulong)(second >> 64));
		Assert.NotEqual((ulong)first, (ulong)second);
	}
}
