using OpenDirectoryDownloader.Shared;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>Characterization tests: pin down what ConcurrentList promises so its internals can change safely.</summary>
public class ConcurrentListTests
{
	[Fact]
	public void BehavesLikeAList()
	{
		ConcurrentList<int> list = [];

		Assert.Empty(list);

		list.Add(1);
		list.Add(2);
		list.Add(4);
		list.Insert(2, 3);
		list.AddRange([5, 6]);

		Assert.Equal([1, 2, 3, 4, 5, 6], list.ToArray());
		Assert.Equal(6, list.Count);
		Assert.Equal(3, list[2]);
		Assert.Equal(2, list.IndexOf(3));
		Assert.Contains(5, list);
		Assert.DoesNotContain(9, list);

		list[0] = 10;
		Assert.True(list.Remove(10));
		Assert.False(list.Remove(10));
		list.RemoveAt(0);

		Assert.Equal([3, 4, 5, 6], list.ToArray());

		int[] copy = new int[5];
		list.CopyTo(copy, 1);
		Assert.Equal([0, 3, 4, 5, 6], copy);

		list.Clear();
		Assert.Empty(list);
	}

	[Fact]
	public void OutOfRangeAccess_Throws()
	{
		ConcurrentList<int> list = [1];

		Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
		Assert.Throws<ArgumentOutOfRangeException>(() => list[1] = 5);
		Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
		Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(2, 5));
	}

	[Fact]
	public void CreatedFromItems_KeepsThem()
	{
		ConcurrentList<string> list = new(new[] { "a", "b", "c" }.Select(x => x));

		Assert.Equal(["a", "b", "c"], list.ToArray());

		list.Add("d");

		Assert.Equal(4, list.Count);
	}

	[Fact]
	public async Task ConcurrentAdds_LoseNothing()
	{
		ConcurrentList<int> list = [];
		const int threads = 8;
		const int perThread = 5_000;

		await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
		{
			for (int i = 0; i < perThread; i++)
			{
				list.Add(t * perThread + i);
			}
		})));

		Assert.Equal(threads * perThread, list.Count);
		Assert.Equal(threads * perThread, list.Distinct().Count());
	}

	[Fact]
	public async Task EnumeratingWhileAnotherThreadAdds_DoesNotThrow()
	{
		ConcurrentList<int> list = [];
		using CancellationTokenSource stop = new();

		Task writer = Task.Run(() =>
		{
			for (int i = 0; i < 50_000; i++)
			{
				list.Add(i);
			}

			stop.Cancel();
		});

		long enumerations = 0;

		while (!stop.IsCancellationRequested)
		{
			// The statistics timer does exactly this against a tree the crawl threads are still filling
			_ = list.Sum(x => (long)x);
			enumerations++;
		}

		await writer;

		Assert.True(enumerations > 0);
		Assert.Equal(50_000, list.Count);
	}
}
