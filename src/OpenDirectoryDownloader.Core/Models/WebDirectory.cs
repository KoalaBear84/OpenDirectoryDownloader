using OpenDirectoryDownloader.Collections;
using System.Diagnostics;

namespace OpenDirectoryDownloader.Models;

/// <summary>
/// One directory of the scanned tree. A parse result is built on a detached instance by a single thread and then
/// copied onto the live tree node in one go (see the crawl engine), so the child lists are plain lists: the only
/// later change to a live node is a subdirectory turning out to be a file, which locks the parent (see
/// <see cref="ReplaceSubdirectoryWithFile"/>).
/// </summary>
[DebuggerDisplay("{Name,nq}, Directories: {Subdirectories.Count,nq}, Files: {Files.Count,nq}")]
public sealed class WebDirectory
{
	public WebDirectory(WebDirectory parentWebDirectory)
	{
		ParentDirectory = parentWebDirectory;
	}

	public WebDirectory ParentDirectory { get; set; }

	/// <summary>Row id in the scan database, 0 while it has not been stored yet.</summary>
	public long Id { get; set; }

	public string Url { get; set; }

	private sealed record CachedUri(string Url, Uri Uri);

	private CachedUri _cachedUri;

	/// <summary>Cached per Url value. Url and Uri are swapped together as one immutable object, so concurrent readers never see a mismatched pair.</summary>
	public Uri Uri
	{
		get
		{
			string url = Url;
			CachedUri cached = _cachedUri;

			if (cached is not null && ReferenceEquals(cached.Url, url))
			{
				return cached.Uri;
			}

			Uri uri = new(url);
			_cachedUri = new CachedUri(url, uri);

			return uri;
		}
	}

	public string Name { get; set; }

	public string Description { get; set; }

	public bool Finished { get; set; }

	public bool Error { get; set; }

	/// <summary>Only meaningful on a parse result: whether the parser recognized the page, even if it found nothing.</summary>
	public bool ParsedSuccessfully { get; set; }

	public List<WebDirectory> Subdirectories { get; set; } = [];

	public List<WebFile> Files { get; set; } = [];

	/// <summary>Name of the parser that produced this directory (or found it, for a subdirectory). Always a string literal, so shared, not a copy per directory.</summary>
	public string Parser { get; set; } = string.Empty;

	public long TotalFileSize
	{
		get
		{
			if (ContentEvicted)
			{
				return CachedTotalFileSize;
			}

			long total = 0;

			foreach (WebDirectory subdirectory in Subdirectories)
			{
				total += subdirectory.TotalFileSize;
			}

			foreach (WebFile file in Files)
			{
				total += file.FileSize ?? 0;
			}

			return total;
		}
	}

	public int TotalFiles
	{
		get
		{
			if (ContentEvicted)
			{
				return CachedTotalFiles;
			}

			int total = Files.Count;

			foreach (WebDirectory subdirectory in Subdirectories)
			{
				total += subdirectory.TotalFiles;
			}

			return total;
		}
	}

	public int TotalDirectories
	{
		get
		{
			if (ContentEvicted)
			{
				return CachedTotalDirectories;
			}

			int total = 0;

			foreach (WebDirectory subdirectory in Subdirectories)
			{
				total += subdirectory.TotalDirectories + (subdirectory.Finished ? 1 : 0);
			}

			return total;
		}
	}

	public int TotalDirectoriesIncludingUnfinished
	{
		get
		{
			if (ContentEvicted)
			{
				return CachedTotalDirectoriesIncludingUnfinished;
			}

			int total = Subdirectories.Count;

			foreach (WebDirectory subdirectory in Subdirectories)
			{
				total += subdirectory.TotalDirectories;
			}

			return total;
		}
	}

	/// <summary>Every file below this directory that is still in memory (evicted subtrees are not, the scan database has those).</summary>
	public IEnumerable<WebFile> AllFiles => Subdirectories.SelectMany(sd => sd.AllFiles).Concat(Files);

	/// <summary>
	/// Cached signature of the direct Files/Subdirectories, used by the symlink loop check so a directory's contents
	/// are not hashed again for every descendant that compares against it. Set once the directory is final.
	/// </summary>
	public UInt128? ContentFingerprint { get; set; }

	private const char FingerprintFieldSeparator = (char)1;
	private const char FingerprintEntrySeparator = (char)2;
	private const char FingerprintSectionSeparator = (char)3;

	/// <summary>A 128-bit hash of the direct Files (name + size) and Subdirectories (name). Only ever compared for equality.</summary>
	public UInt128 ComputeContentFingerprint()
	{
		Hash128 hash = new();

		foreach (WebFile file in Files)
		{
			hash.Add(file.FileName);
			hash.Add(FingerprintFieldSeparator);
			hash.Add(file.FileSize ?? -1);
			hash.Add(FingerprintEntrySeparator);
		}

		hash.Add(FingerprintSectionSeparator);

		foreach (WebDirectory subdirectory in Subdirectories)
		{
			hash.Add(subdirectory.Name);
			hash.Add(FingerprintEntrySeparator);
		}

		return hash.Result;
	}

	/// <summary>
	/// Reference count of outstanding work before this directory's subtree is "closed" and can be dropped from
	/// memory: 1 for its own parse step, plus 1 per subdirectory queued under it and per own file queued for a
	/// size lookup. See <see cref="TryClose"/>.
	/// </summary>
	public int PendingWork = 1;

	public bool ContentEvicted { get; private set; }

	public long CachedTotalFileSize { get; private set; }

	public int CachedTotalFiles { get; private set; }

	public int CachedTotalDirectories { get; private set; }

	public int CachedTotalDirectoriesIncludingUnfinished { get; private set; }

	/// <summary>
	/// Freezes this directory's totals and drops its Files/Subdirectories, once its whole subtree is finished. The
	/// scan database is the source of truth for its contents from then on.
	/// </summary>
	public void EvictContent()
	{
		if (ContentEvicted)
		{
			return;
		}

		CachedTotalFileSize = TotalFileSize;
		CachedTotalFiles = TotalFiles;
		CachedTotalDirectories = TotalDirectories;
		CachedTotalDirectoriesIncludingUnfinished = TotalDirectoriesIncludingUnfinished;

		// Reassigned, not cleared in place: anything still holding the old lists keeps a frozen snapshot
		Files = [];
		Subdirectories = [];

		// Only needed while this directory can still be an open ancestor for the symlink check
		ContentFingerprint = null;

		ContentEvicted = true;
	}

	/// <summary>Reconstructs this directory as an already evicted stub with the given totals (used by --resume).</summary>
	public void RestoreAsClosedStub(int totalFiles, long totalFileSize, int totalDirectories, int totalDirectoriesIncludingUnfinished)
	{
		CachedTotalFiles = totalFiles;
		CachedTotalFileSize = totalFileSize;
		CachedTotalDirectories = totalDirectories;
		CachedTotalDirectoriesIncludingUnfinished = totalDirectoriesIncludingUnfinished;
		ContentEvicted = true;
		PendingWork = 0;
	}

	/// <summary>
	/// Consumes one unit of <paramref name="directory"/>'s PendingWork. If that was the last one, its subtree is
	/// done: it is evicted, which consumes one unit of its parent's PendingWork in turn, up the chain. Interlocked,
	/// so exactly one caller sees a given directory reach zero.
	/// </summary>
	/// <param name="onClosed">Called for every directory that closed, before its content is evicted</param>
	public static void TryClose(WebDirectory directory, Action<WebDirectory> onClosed = null)
	{
		while (directory is not null && Interlocked.Decrement(ref directory.PendingWork) == 0)
		{
			onClosed?.Invoke(directory);
			directory.EvictContent();

			directory = directory.ParentDirectory;
		}
	}

	/// <summary>
	/// The subdirectory turned out to be a file (no HTML, or too big for a listing): moves it from this directory's
	/// subdirectories to its files. Can run on several crawl workers for the same parent, hence the lock.
	/// </summary>
	public WebFile ReplaceSubdirectoryWithFile(WebDirectory subdirectory, long? fileSize)
	{
		WebFile file = new()
		{
			Url = subdirectory.Url,
			FileName = subdirectory.Name,
			FileSize = fileSize,
			ParentDirectory = this
		};

		lock (this)
		{
			Subdirectories.Remove(subdirectory);
			Files.Add(file);
		}

		return file;
	}

	/// <summary>
	/// A deep copy of Url/Name/Description/Finished/Error/Subdirectories/Files. Used by the table parser to give
	/// each &lt;table&gt; on a page its own copy to fill. Does not set ParentDirectory on the copies.
	/// </summary>
	public WebDirectory Clone()
	{
		WebDirectory clone = new(null)
		{
			Url = Url,
			Name = Name,
			Description = Description,
			Finished = Finished,
			Error = Error,
			Subdirectories = new List<WebDirectory>(Subdirectories.Count),
			Files = new List<WebFile>(Files.Count)
		};

		foreach (WebDirectory subdirectory in Subdirectories)
		{
			clone.Subdirectories.Add(subdirectory.Clone());
		}

		foreach (WebFile file in Files)
		{
			clone.Files.Add(file.Clone());
		}

		return clone;
	}
}
