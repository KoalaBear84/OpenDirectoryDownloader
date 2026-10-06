using OpenDirectoryDownloader.Storage;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>The URL list is sorted in chunks and merged; the result must be exactly what one big sort gave.</summary>
public class ExternalUrlSorterTests
{
	private static async IAsyncEnumerable<string> AsAsync(IEnumerable<string> values)
	{
		foreach (string value in values)
		{
			yield return value;
		}

		await Task.CompletedTask;
	}

	private static List<string> Expected(IEnumerable<string> urls)
	{
		List<string> expected = [.. urls.Distinct()];
		expected.Sort(ExternalUrlSorter.Order);

		return expected;
	}

	private static List<string> RandomUrls(int count, int seed)
	{
		Random random = new(seed);
		string[] parts = ["a", "B", "file10", "file2", "File1", "é", "ü", "%20", "10", "9", "dir", "Dir", "x.mkv", "x.MKV", "-", "_", " "];
		List<string> urls = [];

		for (int i = 0; i < count; i++)
		{
			int segments = random.Next(1, 5);
			string url = random.Next(3) == 0 ? "https://other.example.com/" : "http://example.com/";

			for (int segment = 0; segment < segments; segment++)
			{
				url += parts[random.Next(parts.Length)] + parts[random.Next(parts.Length)] + random.Next(0, 120) + (segment < segments - 1 ? "/" : string.Empty);
			}

			urls.Add(url);

			// Duplicates on purpose
			if (random.Next(10) == 0)
			{
				urls.Add(url);
			}
		}

		return urls;
	}

	[Theory]
	[InlineData(1)]
	[InlineData(7)]
	[InlineData(100)]
	[InlineData(1_000_000)]
	public async Task SortDistinct_IsTheSameAsSortingEverything(int chunkSize)
	{
		List<string> urls = RandomUrls(5_000, seed: chunkSize);
		List<string> actual = [];

		await foreach (string url in new ExternalUrlSorter(chunkSize).SortDistinctAsync(AsAsync(urls), TestContext.Current.CancellationToken))
		{
			actual.Add(url);
		}

		Assert.Equal(Expected(urls), actual);
	}

	[Fact]
	public async Task NoUrls_GivesNothing()
	{
		await foreach (string url in new ExternalUrlSorter(10).SortDistinctAsync(AsAsync([]), TestContext.Current.CancellationToken))
		{
			Assert.Fail($"Got {url}");
		}
	}

	[Fact]
	public async Task TemporaryFiles_AreRemoved()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"odd-sorter-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);

		try
		{
			await foreach (string _ in new ExternalUrlSorter(10, directory).SortDistinctAsync(AsAsync(RandomUrls(200, 1)), TestContext.Current.CancellationToken))
			{
			}

			Assert.Empty(Directory.EnumerateFiles(directory));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
