namespace OpenDirectoryDownloader.Scanning;

/// <summary>Counts per HTTP status code. Lock-free: one slot per possible code, updated with Interlocked.</summary>
public sealed class StatusCodeCounter
{
	private const int Size = 1000;

	private readonly int[] _counts = new int[Size];

	public void Add(int statusCode, int count = 1)
	{
		if ((uint)statusCode < Size)
		{
			Interlocked.Add(ref _counts[statusCode], count);
		}
	}

	/// <summary>Codes that were seen, in ascending order.</summary>
	public SortedDictionary<int, int> Snapshot()
	{
		SortedDictionary<int, int> snapshot = [];

		for (int statusCode = 0; statusCode < Size; statusCode++)
		{
			int count = Volatile.Read(ref _counts[statusCode]);

			if (count != 0)
			{
				snapshot[statusCode] = count;
			}
		}

		return snapshot;
	}
}
