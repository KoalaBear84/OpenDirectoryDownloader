using System.Collections.Concurrent;

namespace OpenDirectoryDownloader.Scanning;

/// <summary>
/// Per scan state of the site parsers (a detected Google Drive index type, an access token, a CSRF token, ...),
/// one typed object per site. It replaces the string dictionary and the static fields the parsers used to share.
/// </summary>
public sealed class SiteStateStore
{
	private readonly ConcurrentDictionary<Type, object> _states = new();

	/// <summary>The state of type <typeparamref name="T"/>, created on first use.</summary>
	public T Get<T>() where T : class, new() => (T)_states.GetOrAdd(typeof(T), static _ => new T());

	/// <summary>The state of type <typeparamref name="T"/> if a parser already created it.</summary>
	public T Find<T>() where T : class => _states.TryGetValue(typeof(T), out object state) ? (T)state : null;
}
