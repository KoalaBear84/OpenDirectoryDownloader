using OpenDirectoryDownloader.Helpers;
using System.Runtime.CompilerServices;
using System.Text;

namespace OpenDirectoryDownloader.Storage;

/// <summary>
/// Sorts a stream of URLs naturally and drops duplicates, like sorting them all in a list did, without holding them
/// all in memory: chunks are sorted and written to temporary files, which are then merged. A scan small enough for
/// one chunk is sorted in memory only.
/// </summary>
public sealed class ExternalUrlSorter
{
	/// <summary>Natural order, ties between different strings broken ordinally: a total order, so the result does not depend on the input order.</summary>
	public static readonly Comparison<string> Order = [MethodImpl(MethodImplOptions.AggressiveOptimization)] (x, y) =>
	{
		int result = NaturalSortStringComparer.InvariantCulture.Compare(x, y);

		return result != 0 ? result : string.CompareOrdinal(x, y);
	};

	private static readonly IComparer<string> OrderComparer = Comparer<string>.Create(Order);

	private readonly int _chunkSize;
	private readonly string _temporaryDirectory;

	/// <param name="chunkSize">URLs per chunk sorted in memory</param>
	/// <param name="temporaryDirectory">Where the sorted chunks go, the system's temporary directory by default</param>
	public ExternalUrlSorter(int chunkSize = 1_000_000, string temporaryDirectory = null)
	{
		_chunkSize = Math.Max(1, chunkSize);
		_temporaryDirectory = temporaryDirectory ?? Path.GetTempPath();
	}

	/// <summary>The distinct URLs of <paramref name="urls"/>, sorted.</summary>
	public async IAsyncEnumerable<string> SortDistinctAsync(IAsyncEnumerable<string> urls, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		List<string> chunk = new(Math.Min(_chunkSize, 65_536));
		List<string> runFiles = [];

		try
		{
			await foreach (string url in urls.WithCancellation(cancellationToken))
			{
				chunk.Add(url);

				if (chunk.Count >= _chunkSize)
				{
					runFiles.Add(await WriteRunAsync(chunk, cancellationToken));
					chunk.Clear();
				}
			}

			if (runFiles.Count == 0)
			{
				Sort(chunk);

				foreach (string url in Distinct(chunk))
				{
					yield return url;
				}

				yield break;
			}

			if (chunk.Count > 0)
			{
				runFiles.Add(await WriteRunAsync(chunk, cancellationToken));
				chunk.Clear();
			}

			chunk.TrimExcess();

			foreach (string url in Merge(runFiles))
			{
				cancellationToken.ThrowIfCancellationRequested();

				yield return url;
			}
		}
		finally
		{
			foreach (string runFile in runFiles)
			{
				try
				{
					File.Delete(runFile);
				}
				catch (IOException)
				{
					// Best effort, it is in the temporary directory
				}
			}
		}
	}

	private static IEnumerable<string> Distinct(IEnumerable<string> sorted)
	{
		string previous = null;

		foreach (string url in sorted)
		{
			if (previous is null || !string.Equals(previous, url, StringComparison.Ordinal))
			{
				yield return url;
			}

			previous = url;
		}
	}

	/// <summary>
	/// Sorts parts of the list in parallel and merges them pairwise, also in parallel. The order is total (equal
	/// means the same string), so the result is exactly what one sort gives.
	/// </summary>
	internal static void Sort(List<string> urls)
	{
		const int MinimumPartLength = 16_384;

		int parts = Math.Min(Environment.ProcessorCount, urls.Count / MinimumPartLength);

		if (parts < 2)
		{
			urls.Sort(Order);
			return;
		}

		string[] source = [.. urls];
		string[] target = new string[source.Length];

		// Part boundaries, merged pairwise until one part is left
		List<int> bounds = [.. Enumerable.Range(0, parts + 1).Select(part => (int)((long)source.Length * part / parts))];

		Parallel.For(0, parts, part => source.AsSpan(bounds[part], bounds[part + 1] - bounds[part]).Sort(Order));

		while (bounds.Count > 2)
		{
			int pairs = (bounds.Count - 1) / 2;
			string[] from = source;
			string[] to = target;
			List<int> current = bounds;

			Parallel.For(0, pairs, pair => Merge(from, current[2 * pair], current[2 * pair + 1], current[2 * pair + 2], to));

			// An odd part out is copied as it is
			if ((bounds.Count - 1) % 2 == 1)
			{
				Array.Copy(source, bounds[^2], target, bounds[^2], bounds[^1] - bounds[^2]);
			}

			List<int> merged = [];

			for (int i = 0; i < bounds.Count; i += 2)
			{
				merged.Add(bounds[i]);
			}

			if (merged[^1] != bounds[^1])
			{
				merged.Add(bounds[^1]);
			}

			bounds = merged;
			(source, target) = (target, source);
		}

		for (int i = 0; i < source.Length; i++)
		{
			urls[i] = source[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void Merge(string[] source, int start, int middle, int end, string[] target)
	{
		int left = start;
		int right = middle;
		int position = start;

		while (left < middle && right < end)
		{
			target[position++] = Order(source[right], source[left]) < 0 ? source[right++] : source[left++];
		}

		Array.Copy(source, left, target, position, middle - left);
		position += middle - left;
		Array.Copy(source, right, target, position, end - right);
	}

	private async Task<string> WriteRunAsync(List<string> chunk, CancellationToken cancellationToken)
	{
		Sort(chunk);

		string path = Path.Combine(_temporaryDirectory, $"odd-urls-{Guid.NewGuid():N}.tmp");

		await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1 << 20, FileOptions.SequentialScan);
		using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

		foreach (string url in Distinct(chunk))
		{
			cancellationToken.ThrowIfCancellationRequested();
			writer.Write(url);
		}

		return path;
	}

	/// <summary>K-way merge of the sorted run files</summary>
	private static IEnumerable<string> Merge(List<string> runFiles)
	{
		List<BinaryReader> readers = [];

		try
		{
			PriorityQueue<int, string> queue = new(OrderComparer);

			for (int i = 0; i < runFiles.Count; i++)
			{
				BinaryReader reader = new(new FileStream(runFiles[i], FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.SequentialScan), Encoding.UTF8);
				readers.Add(reader);

				if (TryRead(reader, out string first))
				{
					queue.Enqueue(i, first);
				}
			}

			string previous = null;

			while (queue.TryDequeue(out int run, out string url))
			{
				if (previous is null || !string.Equals(previous, url, StringComparison.Ordinal))
				{
					yield return url;
				}

				previous = url;

				if (TryRead(readers[run], out string next))
				{
					queue.Enqueue(run, next);
				}
			}
		}
		finally
		{
			foreach (BinaryReader reader in readers)
			{
				reader.Dispose();
			}
		}
	}

	private static bool TryRead(BinaryReader reader, out string value)
	{
		if (reader.BaseStream.Position >= reader.BaseStream.Length)
		{
			value = null;
			return false;
		}

		value = reader.ReadString();
		return true;
	}
}
