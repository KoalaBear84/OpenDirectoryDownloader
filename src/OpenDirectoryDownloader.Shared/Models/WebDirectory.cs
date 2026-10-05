using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;

namespace OpenDirectoryDownloader.Shared.Models;

[DebuggerDisplay("{Name,nq}, Directories: {Subdirectories.Count,nq}, Files: {Files.Count,nq}")]
public class WebDirectory
{
	public WebDirectory(WebDirectory parentWebDirectory)
	{
		ParentDirectory = parentWebDirectory;
	}

	/// <summary>
	/// Used by System.Text.Json deserialization only. ParentDirectory is [JsonIgnore]d (it would create a
	/// cyclical graph) so no constructor parameter can bind to it - callers reconstructing a WebDirectory
	/// from JSON (see Library.LoadSessionJson, DirectoryParser's WebDirectory clone-via-round-trip) must set
	/// ParentDirectory themselves afterward, same as they already do for a tree walked from a freshly
	/// deserialized Session.
	/// </summary>
	[JsonConstructor]
	public WebDirectory()
	{
	}

	[JsonIgnore]
	public WebDirectory ParentDirectory { get; set; }

	public string Url { get; set; }

	[JsonIgnore]
	public Uri Uri => new(Url);

	public string Name { get; set; }

	public string Description { get; set; }

	public bool Finished { get; set; }

	[JsonIgnore]
	public bool ParsedSuccessfully { get; set; }

	public ConcurrentList<WebDirectory> Subdirectories { get; set; } = [];

	public ConcurrentList<WebFile> Files { get; set; } = [];

	public bool Error { get; set; }

	[JsonIgnore]
	public long TotalFileSize => ContentEvicted ? CachedTotalFileSize : Subdirectories.Sum(sd => sd.TotalFileSize) + Files.Sum(f => f.FileSize ?? 0);

	[JsonIgnore]
	public int TotalFiles => ContentEvicted ? CachedTotalFiles : Subdirectories.Sum(sd => sd.TotalFiles) + Files.Count;

	[JsonIgnore]
	public int TotalDirectories => ContentEvicted ? CachedTotalDirectories : Subdirectories.Sum(sd => sd.TotalDirectories) + Subdirectories.Count(sd => sd.Finished);

	[JsonIgnore]
	public int TotalDirectoriesIncludingUnfinished => ContentEvicted ? CachedTotalDirectoriesIncludingUnfinished : Subdirectories.Sum(sd => sd.TotalDirectories) + Subdirectories.Count;

	/// <summary>
	/// NOTE: once a directory's content has been evicted (see EvictContent/ContentEvicted, issue #56 phase
	/// 4), this and AllFiles silently stop including anything below the evicted directory - eviction only
	/// runs when a caller (OpenDirectoryIndexer) opted into it, and that same caller is responsible for
	/// getting file listings from the scan database instead once it has evicted anything.
	/// </summary>
	[JsonIgnore]
	public IEnumerable<string> Urls => Files.Select(f => f.Url);

	[JsonIgnore]
	public IEnumerable<string> AllFileUrls => Subdirectories.SelectMany(sd => sd.AllFileUrls).Concat(Urls).OrderBy(url => url);

	[JsonIgnore]
	public IEnumerable<WebFile> AllFiles => Subdirectories.SelectMany(sd => sd.AllFiles).Concat(Files);

	[JsonIgnore]
	public string Parser { get; set; } = string.Empty;

	[JsonIgnore]
	public DateTimeOffset StartTime { get; set; }
	[JsonIgnore]
	public DateTimeOffset FinishTime { get; set; }

	[JsonIgnore]
	public int HeaderCount { get; set; }

	[JsonIgnore]
	public string CancellationReason { get; set; }

	/// <summary>
	/// Cached signature of the direct Files/Subdirectories, used by the recursive/symlink loop check
	/// (see DirectoryParser.CheckSymlinks) to avoid re-serializing a directory's contents for every
	/// descendant, potentially many levels down, that checks against it as an ancestor. Set once this
	/// directory's Files/Subdirectories are final (see OpenDirectoryIndexer.AddProcessedWebDirectory);
	/// falls back to computing on demand if it wasn't cached (e.g. for a not-yet-finalized directory).
	/// </summary>
	[JsonIgnore]
	public string ContentFingerprint { get; set; }

	private const char FingerprintFieldSeparator = (char)1;
	private const char FingerprintEntrySeparator = (char)2;
	private const char FingerprintSectionSeparator = (char)3;

	public string ComputeContentFingerprint()
	{
		StringBuilder stringBuilder = new();

		foreach (WebFile file in Files)
		{
			stringBuilder.Append(file.FileName).Append(FingerprintFieldSeparator).Append(file.FileSize).Append(FingerprintEntrySeparator);
		}

		stringBuilder.Append(FingerprintSectionSeparator);

		foreach (WebDirectory subdirectory in Subdirectories)
		{
			stringBuilder.Append(subdirectory.Name).Append(FingerprintEntrySeparator);
		}

		return stringBuilder.ToString();
	}

	/// <summary>
	/// Reference count of outstanding work that must complete before this directory's own subtree is
	/// "closed" and safe to evict (issue #56 phase 4): starts at 1 for this directory's own parse step,
	/// plus 1 for every subdirectory enqueued under it and every one of its own files queued for a
	/// file-size lookup. See OpenDirectoryIndexer.TryCloseDirectory for the decrement/eviction algorithm.
	/// Only meaningful when eviction is enabled; otherwise it is incremented/decremented for nothing.
	/// </summary>
	[JsonIgnore]
	public int PendingWork = 1;

	[JsonIgnore]
	public bool ContentEvicted { get; private set; }

	[JsonIgnore]
	public long CachedTotalFileSize { get; private set; }

	[JsonIgnore]
	public int CachedTotalFiles { get; private set; }

	[JsonIgnore]
	public int CachedTotalDirectories { get; private set; }

	[JsonIgnore]
	public int CachedTotalDirectoriesIncludingUnfinished { get; private set; }

	/// <summary>
	/// Freezes this directory's aggregate totals and drops its (potentially large) Files/Subdirectories
	/// collections, once its entire subtree has finished (PendingWork reached 0). Safe to call only then:
	/// nothing can still be treating this directory as an active ancestor for the symlink check at that
	/// point (see DirectoryParser.CheckSymlinks), since that requires a still-open descendant to exist.
	/// The scan database, not this in-memory object, becomes the source of truth for this directory's
	/// files/subdirectories from this point on.
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

		// Reassigned, not cleared in place: anything still (incorrectly) holding a reference to the old
		// lists keeps working against a frozen snapshot instead of racing an in-place Clear().
		Files = [];
		Subdirectories = [];

		ContentEvicted = true;
	}

	/// <summary>
	/// Directly reconstructs this directory as an already-evicted stub with the given totals, without ever
	/// materializing its Files/Subdirectories at all (issue #56 phase 6: --resume). Used when a previous
	/// run's scan database shows this directory's whole subtree already finished and fully closed - there
	/// is no live data to freeze via EvictContent, only the aggregate totals computed from the database.
	/// </summary>
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
	/// Consumes one unit of <paramref name="directory"/>'s PendingWork. If that was the last outstanding
	/// unit, the directory's whole subtree is done: it's evicted, which consumes one unit of its own
	/// parent's PendingWork in turn, continuing up the chain for as long as each ancestor's count also
	/// reaches zero. Safe to call concurrently from multiple directories/files closing at once - the
	/// Interlocked.Decrement below guarantees exactly one caller ever observes a given directory's count
	/// hit zero, so EvictContent runs at most once per directory.
	/// </summary>
	public static void TryClose(WebDirectory directory)
	{
		while (directory is not null && Interlocked.Decrement(ref directory.PendingWork) == 0)
		{
			directory.EvictContent();

			directory = directory.ParentDirectory;
		}
	}

	/// <summary>
	/// A deep copy of Url/Name/Description/Finished/Error/Subdirectories/Files - the same fields a JSON
	/// round-trip through this type would preserve (everything else here is [JsonIgnore]d). Used by
	/// DirectoryParser.ParseTablesDirectoryListing to give each `&lt;table&gt;` candidate on a page its own
	/// independent copy to populate, without constructing and tearing down a JSON document to do it.
	/// Deliberately does not fix up ParentDirectory on any cloned subdirectory (including nested ones) -
	/// same as the JSON round-trip it replaces, where [JsonIgnore] means every level comes back null;
	/// callers that need the top-level ParentDirectory set do so themselves afterward.
	/// </summary>
	public WebDirectory Clone()
	{
		return new WebDirectory
		{
			Url = Url,
			Name = Name,
			Description = Description,
			Finished = Finished,
			Error = Error,
			Subdirectories = new ConcurrentList<WebDirectory>(Subdirectories.Select(sd => sd.Clone())),
			Files = new ConcurrentList<WebFile>(Files.Select(f => f.Clone()))
		};
	}
}
