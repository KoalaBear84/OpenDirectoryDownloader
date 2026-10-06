using OpenDirectoryDownloader.Threading;

namespace OpenDirectoryDownloader.Parsing.Sites;

// The state the site parsers keep for the rest of a scan, one object per scan (ScanSession.SiteState).

/// <summary>The password of a password protected index (AList, Blitzfiles, the Google Drive indexes), asked once per scan.</summary>
public sealed class IndexPasswordState
{
	/// <summary>Whether the password was checked already (it can be known to be empty).</summary>
	public bool IsKnown { get; private set; }

	public string Password { get; private set; }

	public void Set(string password)
	{
		Password = password;
		IsKnown = true;
	}

	public void Clear()
	{
		Password = null;
		IsKnown = false;
	}
}

/// <summary>Which Google Drive index front end the site is, detected from its scripts on the first page.</summary>
public sealed class GoogleDriveIndexState
{
	/// <summary>One of the <see cref="GDIndex.GoogleDriveIndexMapping"/> names, null while unknown.</summary>
	public string IndexType { get; set; }

	/// <summary>The root folder id of a GdIndex site; set once the first page was read, after that every directory is read through its API.</summary>
	public string GdIndexRootId { get; set; }

	/// <summary>A Google Drive index answers 404 for folders it is still loading, so 404 is not final for it.</summary>
	public bool IsGoogleDriveIndex { get; set; }

	/// <summary>Scripts already checked, so every directory page does not download them again.</summary>
	public System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string>>> CheckedScripts { get; } = new(StringComparer.Ordinal);

	/// <summary>All the index front ends allow about one request per second.</summary>
	public RateLimiter RateLimiter { get; } = new(1, TimeSpan.FromSeconds(1));
}
