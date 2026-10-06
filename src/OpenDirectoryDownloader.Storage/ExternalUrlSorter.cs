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
	public static readonly Comparison<string> Order = (x, y) =>
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
				chunk.Sort(Order);

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

	private async Task<string> WriteRunAsync(List<string> chunk, CancellationToken cancellationToken)
	{
		chunk.Sort(Order);

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
