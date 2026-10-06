using System.Text;

namespace OpenDirectoryDownloader.Urls;

/// <summary>
/// Reads and edits the query string of a URL (or a bare query string).
/// </summary>
/// <remarks>
/// This used to be a NameValueCollection (a hashtable and an array list per instance), created two or three times
/// for every link on every page. It keeps exactly what that did, quirks included, because the parsers' results
/// depend on them:
/// <list type="bullet">
/// <item>Keys are case-insensitive (invariant culture) and keep the order they first appeared in.</item>
/// <item>Without a '?', the whole string is split on '&amp;' and ';' anyway.</item>
/// <item>Values stay as they were in the URL (not unescaped), and <see cref="ToString"/> escapes them (again).</item>
/// <item>The indexer joins the values of a repeated key with ','.</item>
/// </list>
/// </remarks>
public sealed class UrlEncodingParser
{
	private const StringComparison KeyComparison = StringComparison.InvariantCultureIgnoreCase;

	/// <summary>The original URL or query, rewritten with the current query by <see cref="ToString"/>.</summary>
	private readonly string _url;

	private readonly List<Entry> _entries = [];

	/// <param name="queryStringOrUrl">A query string or a full URL</param>
	public UrlEncodingParser(string queryStringOrUrl = null)
	{
		_url = string.Empty;

		if (string.IsNullOrEmpty(queryStringOrUrl))
		{
			return;
		}

		_url = queryStringOrUrl;

		ReadOnlySpan<char> query = queryStringOrUrl;
		int questionMark = query.IndexOf('?');

		if (questionMark > -1)
		{
			query = query[(questionMark + 1)..];
		}

		while (true)
		{
			int separator = query.IndexOfAny('&', ';');
			ReadOnlySpan<char> pair = separator < 0 ? query : query[..separator];
			int equals = pair.IndexOf('=');

			if (equals > 0)
			{
				Add(pair[..equals].ToString(), pair[(equals + 1)..].ToString());
			}

			if (separator < 0)
			{
				break;
			}

			query = query[(separator + 1)..];
		}
	}

	/// <summary>Number of distinct keys</summary>
	public int Count => _entries.Count;

	public string[] AllKeys
	{
		get
		{
			string[] keys = new string[_entries.Count];

			for (int i = 0; i < keys.Length; i++)
			{
				keys[i] = _entries[i].Key;
			}

			return keys;
		}
	}

	/// <summary>The values of <paramref name="key"/> joined with ',', or null when the key is not there.</summary>
	public string this[string key]
	{
		get => Get(key);
		set
		{
			Remove(key);
			Add(key, value);
		}
	}

	public string Get(string key)
	{
		Entry entry = Find(key);

		if (entry is null)
		{
			return null;
		}

		return entry.Values.Count == 1 ? entry.Values[0] : string.Join(',', entry.Values);
	}

	public string[] GetValues(string key) => Find(key)?.Values.ToArray();

	public void Add(string key, string value)
	{
		Entry entry = Find(key);

		if (entry is null)
		{
			entry = new Entry(key);
			_entries.Add(entry);
		}

		entry.Values.Add(value);
	}

	public void Remove(string key)
	{
		for (int i = 0; i < _entries.Count; i++)
		{
			if (string.Equals(_entries[i].Key, key, KeyComparison))
			{
				_entries.RemoveAt(i);
				return;
			}
		}
	}

	/// <summary>The URL (or query) with the current, escaped, query string.</summary>
	public override string ToString()
	{
		StringBuilder query = new();

		foreach (Entry entry in _entries)
		{
			foreach (string value in entry.Values)
			{
				if (query.Length > 0)
				{
					query.Append('&');
				}

				query.Append(entry.Key).Append('=').Append(Uri.EscapeDataString(value));
			}
		}

		string queryText = query.ToString().Trim('&');

		if (string.IsNullOrEmpty(_url))
		{
			return queryText;
		}

		int questionMark = _url.IndexOf('?');
		string beforeQuery = questionMark < 0 ? _url : _url[..questionMark];

		return string.IsNullOrWhiteSpace(queryText) ? beforeQuery : $"{beforeQuery}?{queryText}";
	}

	private Entry Find(string key)
	{
		foreach (Entry entry in _entries)
		{
			if (string.Equals(entry.Key, key, KeyComparison))
			{
				return entry;
			}
		}

		return null;
	}

	private sealed class Entry(string key)
	{
		public string Key { get; } = key;

		public List<string> Values { get; } = new(1);
	}
}
