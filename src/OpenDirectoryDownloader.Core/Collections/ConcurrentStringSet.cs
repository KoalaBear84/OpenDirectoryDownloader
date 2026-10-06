using System.Collections;
using System.Collections.Concurrent;

namespace OpenDirectoryDownloader.Collections;

/// <summary>A thread-safe set of strings (ordinal), for the small sets a scan keeps: URLs with errors, browser attempts, alternative URLs.</summary>
public sealed class ConcurrentStringSet : IReadOnlyCollection<string>
{
	private readonly ConcurrentDictionary<string, byte> _items = new(StringComparer.Ordinal);

	public int Count => _items.Count;

	/// <summary>Returns false when it was already in the set.</summary>
	public bool Add(string value) => _items.TryAdd(value, 0);

	public bool Contains(string value) => _items.ContainsKey(value);

	public bool Remove(string value) => _items.TryRemove(value, out _);

	public IEnumerator<string> GetEnumerator() => _items.Keys.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
