using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;

namespace OpenDirectoryDownloader.Storage;

/// <summary>
/// Reconstructs an in-memory WebDirectory tree from a previous run's scan database, to continue an
/// interrupted scan (issue #56 phase 6: --resume) instead of starting over. Built bottom-up: a directory
/// whose entire subtree is already finished is restored directly as an evicted stub (its Files/
/// Subdirectories are never materialized at all, preserving the eviction memory benefit across a resume of
/// a mostly-finished scan); everything still open around the edges is rebuilt as a live WebDirectory ready
/// to keep crawling exactly where it left off.
///
/// Session-level counters (HTTP traffic/requests, status codes, errors, skipped) are handled separately
/// from the tree: they're periodically snapshotted to the database (ScanDatabase.MirrorSessionStats, from
/// OpenDirectoryIndexer.TimerStatistics_Elapsed) and restored here-adjacent, in OpenDirectoryIndexer's own
/// --resume handling, from the last snapshot before the previous run stopped - so they reflect the whole
/// scan, not just the resumed portion, accurate as of that last periodic snapshot (at most one interval,
/// currently 30s/5s, of the very latest progress can be missing after a hard kill rather than a clean
/// --resume pause).
///
/// Known limitations, accepted as reasonable trade-offs rather than fixed:
/// - A directory that was mid-flight when the process was killed - past AddProcessedWebDirectory's mirror
///   calls but before its own Finished flag was persisted - may leave orphaned child rows in the database
///   from that one directory. It's re-crawled from scratch on resume like any other unfinished directory
///   (the same is true of a directory retried after an error, via retryErrors below); if the remote content
///   hasn't changed this is harmless (the same rows get overwritten), and if it has, at most that one
///   directory's stale, no-longer-real entries could reappear in the DB-sourced output.
/// - --exact-file-sizes can't tell "already resolved" apart from "already exact" across a resume; a file
///   that already has some size value is treated as resolved and is not re-queued for a HEAD request.
/// </summary>
public static class ScanResume
{
	public sealed record Result(WebDirectory Root, List<WebDirectory> DirectoriesToRequeue, List<WebFile> FilesToRequeueForSize, ProcessedUrlSet ProcessedUrls);

	/// <param name="retryErrors">
	/// When true, a directory that finished with Error set (issue #56 phase 6: asked about interactively,
	/// or via --retry-errors, when any are found - see OpenDirectoryIndexer's --resume handling) is treated
	/// the same as an unfinished one: reset and requeued for a fresh attempt, rather than kept as-is.
	/// </param>
	public static async Task<Result> BuildAsync(ScanDatabase scanDatabase, string rootUrl, bool skipFileSizeLookups, bool retryErrors = false)
	{
		// One compact entry per directory, with its children as a linked list and its file totals attached,
		// instead of the previous parallel structures (record list, ParentUrl-grouped lists, and a file
		// aggregate dictionary keyed by yet another copy of every URL string): on a scan with millions of
		// directories those copies, not the tree itself, were what made resuming spike in memory.
		Dictionary<string, ResumeEntry> byUrl = [];

		await scanDatabase.ForEachDirectoryAsync(d => byUrl.Add(d.Url, new ResumeEntry
		{
			Url = d.Url,
			Name = d.Name,
			Description = d.Description,
			Finished = d.Finished,
			Error = d.Error
		}));

		if (!byUrl.ContainsKey(rootUrl))
		{
			return null;
		}

		// Second, lighter pass over the same table (same order) to link children to parents; the ParentUrl
		// strings are only looked up and then dropped, never kept
		await scanDatabase.ForEachDirectoryParentAsync((url, parentUrl) =>
		{
			if (byUrl.TryGetValue(parentUrl, out ResumeEntry parentEntry))
			{
				parentEntry.AddChild(byUrl[url]);
			}
		});

		await scanDatabase.ForEachFileAggregateAsync((directoryUrl, aggregate) =>
		{
			if (byUrl.TryGetValue(directoryUrl, out ResumeEntry entry))
			{
				entry.FileCount = aggregate.Count;
				entry.FileTotalSize = aggregate.TotalSize;
				entry.FileNullSizeCount = aggregate.NullSizeCount;
			}
		});

		Result result = new(null, [], [], new ProcessedUrlSet());

		async Task<(WebDirectory Node, bool Closed)> BuildAsync(ResumeEntry record, WebDirectory parent)
		{
			string url = record.Url;
			bool retrying = record.Finished && record.Error && retryErrors;

			WebDirectory node = new(parent)
			{
				Url = record.Url,
				Name = record.Name,
				Description = record.Description,
				// A retried directory gets a clean slate - AddProcessedWebDirectory sets this fresh (true
				// or false) once it's actually reprocessed, whichever the outcome turns out to be this time.
				Error = retrying ? false : record.Error
			};

			if (!record.Finished || retrying)
			{
				// Not (yet) processed, interrupted mid-flight, or being retried after an error - start
				// clean, exactly like a directory discovered for the first time. Deliberately does not
				// descend into whatever children a previous attempt might have already mirrored; see the
				// class-level remarks.
				node.Finished = false;
				node.PendingWork = 1;
				result.DirectoriesToRequeue.Add(node);

				return (node, false);
			}

			node.Finished = true;
			result.ProcessedUrls.Add(url);

			bool hasOwnPendingFileSizes = !skipFileSizeLookups && record.FileNullSizeCount > 0;

			List<(WebDirectory Node, bool Closed)> children = [];

			for (ResumeEntry childRecord = record.FirstChild; childRecord is not null; childRecord = childRecord.NextSibling)
			{
				children.Add(await BuildAsync(childRecord, node));
			}

			bool isClosed = !hasOwnPendingFileSizes && children.All(c => c.Closed);

			if (isClosed)
			{
				int totalDirectories = children.Count + children.Sum(c => c.Node.CachedTotalDirectories);
				int totalFiles = (int)record.FileCount + children.Sum(c => c.Node.CachedTotalFiles);
				long totalFileSize = record.FileTotalSize + children.Sum(c => c.Node.CachedTotalFileSize);

				node.RestoreAsClosedStub(totalFiles, totalFileSize, totalDirectories, totalDirectories);

				return (node, true);
			}

			int pendingFileSizeCount = 0;

			foreach (ScanDatabase.MirroredFile file in await scanDatabase.GetFilesAsync(url))
			{
				WebFile webFile = new()
				{
					Url = file.Url,
					FileName = file.FileName,
					FileSize = file.FileSize,
					Description = file.Description,
					ParentDirectory = node
				};

				node.Files.Add(webFile);

				if (!skipFileSizeLookups && webFile.FileSize is null)
				{
					result.FilesToRequeueForSize.Add(webFile);
					pendingFileSizeCount++;
				}
			}

			foreach ((WebDirectory childNode, bool _) in children)
			{
				node.Subdirectories.Add(childNode);
			}

			node.ContentFingerprint = node.ComputeContentFingerprint();
			node.PendingWork = children.Count(c => !c.Closed) + pendingFileSizeCount;

			return (node, false);
		}

		(WebDirectory root, bool _) = await BuildAsync(byUrl[rootUrl], null);

		return result with { Root = root };
	}

	/// <summary>Everything BuildAsync needs to know about one directory while rebuilding, and nothing else.</summary>
	private sealed class ResumeEntry
	{
		public string Url;
		public string Name;
		public string Description;
		public bool Finished;
		public bool Error;
		public long FileCount;
		public long FileTotalSize;
		public long FileNullSizeCount;
		public ResumeEntry FirstChild;
		public ResumeEntry NextSibling;
		private ResumeEntry _lastChild;

		/// <summary>Appends, so children keep the order the database returned them in</summary>
		public void AddChild(ResumeEntry child)
		{
			if (_lastChild is null)
			{
				FirstChild = child;
			}
			else
			{
				_lastChild.NextSibling = child;
			}

			_lastChild = child;
		}
	}
}
