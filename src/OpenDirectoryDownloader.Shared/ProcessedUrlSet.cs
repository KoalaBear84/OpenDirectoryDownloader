namespace OpenDirectoryDownloader.Shared;

/// <summary>
/// The set of directory URLs the crawler has already started or finished. It holds a 128-bit hash per URL
/// instead of the URL string: the set lives for the whole scan, and with millions of directories the strings
/// (typically 100+ bytes each, plus dictionary node overhead) were the largest thing --evict-memory could
/// not free. Operations happen once per directory, not per file, so a plain lock is cheaper than a concurrent dictionary.
/// </summary>
public sealed class ProcessedUrlSet
{
	private readonly HashSet<UInt128> _hashes = [];
	private readonly Lock _lock = new();

	public int Count
	{
		get
		{
			lock (_lock)
			{
				return _hashes.Count;
			}
		}
	}

	public bool Contains(string url)
	{
		UInt128 hash = Hash128.Of(url);

		lock (_lock)
		{
			return _hashes.Contains(hash);
		}
	}

	/// <summary>Returns false if the URL was already in the set.</summary>
	public bool Add(string url)
	{
		UInt128 hash = Hash128.Of(url);

		lock (_lock)
		{
			return _hashes.Add(hash);
		}
	}

	public bool Remove(string url)
	{
		UInt128 hash = Hash128.Of(url);

		lock (_lock)
		{
			return _hashes.Remove(hash);
		}
	}
}
