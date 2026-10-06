using OpenDirectoryDownloader.Collections;
using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.Storage;

/// <summary>
/// Rebuilds the in-memory tree of an interrupted scan from its database (--resume). A directory whose whole
/// subtree is finished comes back as a closed stub with only its totals (its files and subdirectories are never
/// loaded); the open edges come back as live directories to continue crawling where the scan stopped.
/// </summary>
/// <remarks>
/// A directory that was not finished is crawled again from scratch: whatever an earlier attempt already stored
/// below it is removed first, so nothing is stored twice. With --exact-file-sizes a file that already has a size
/// is not looked up again.
/// </remarks>
public static class ScanResume
{
	public sealed record Result(WebDirectory Root, List<WebDirectory> DirectoriesToRequeue, List<WebFile> FilesToRequeueForSize, ProcessedUrlSet ProcessedUrls);

	/// <param name="retryErrors">Treat directories that finished with an error as unfinished: crawl them again</param>
	/// <returns>Null when the database has no directory with <paramref name="rootUrl"/></returns>
	public static async Task<Result> BuildAsync(ScanDatabase scanDatabase, string rootUrl, bool skipFileSizeLookups, bool retryErrors = false)
	{
		Dictionary<long, ResumeEntry> byId = [];
		ResumeEntry rootEntry = null;

		foreach (ScanDatabase.StoredDirectory directory in await scanDatabase.GetAllDirectoriesAsync())
		{
			ResumeEntry entry = new() { Directory = directory };
			byId.Add(directory.Id, entry);

			if (directory.ParentId is long parentId && byId.TryGetValue(parentId, out ResumeEntry parentEntry))
			{
				parentEntry.AddChild(entry);
			}
			else if (directory.ParentId is null && rootEntry is null && directory.Url == rootUrl)
			{
				rootEntry = entry;
			}
		}

		if (rootEntry is null)
		{
			return null;
		}

		foreach ((long directoryId, ScanDatabase.FileAggregate aggregate) in await scanDatabase.GetFileAggregatesByDirectoryAsync())
		{
			if (byId.TryGetValue(directoryId, out ResumeEntry entry))
			{
				entry.Files = aggregate;
			}
		}

		Result result = new(null, [], [], new ProcessedUrlSet());

		// Directories that stay open, in the order their files are loaded below (children before their parent)
		List<(WebDirectory Node, ScanDatabase.StoredDirectory Record, int OpenChildren)> openDirectories = [];

		// Synchronous: a deep tree must not cost a large async state machine per level
		(WebDirectory Node, bool Closed) Build(ResumeEntry entry, WebDirectory parent)
		{
			ScanDatabase.StoredDirectory record = entry.Directory;
			bool retrying = record.Finished && record.Error && retryErrors;

			WebDirectory node = new(parent)
			{
				Id = record.Id,
				Url = record.Url,
				Name = record.Name,
				Description = record.Description,
				Parser = record.Parser ?? string.Empty,
				// A retried directory starts clean, it gets its error state again when it is processed
				Error = !retrying && record.Error
			};

			if (!record.Finished || retrying)
			{
				// Not processed yet, interrupted halfway, or retried: like a directory found for the first time
				node.Finished = false;
				node.PendingWork = 1;
				result.DirectoriesToRequeue.Add(node);

				if (entry.FirstChild is not null || entry.Files is not null)
				{
					scanDatabase.ResetSubtree(record.Id);
				}

				return (node, false);
			}

			node.Finished = true;
			result.ProcessedUrls.Add(record.Url);

			bool hasOwnPendingFileSizes = !skipFileSizeLookups && entry.Files?.NullSizeCount > 0;

			List<(WebDirectory Node, bool Closed)> children = [];

			for (ResumeEntry child = entry.FirstChild; child is not null; child = child.NextSibling)
			{
				children.Add(Build(child, node));
			}

			if (!hasOwnPendingFileSizes && children.TrueForAll(c => c.Closed))
			{
				int totalDirectories = children.Count + children.Sum(c => c.Node.CachedTotalDirectories);
				int totalFiles = (int)(entry.Files?.Count ?? 0) + children.Sum(c => c.Node.CachedTotalFiles);
				long totalFileSize = (entry.Files?.TotalSize ?? 0) + children.Sum(c => c.Node.CachedTotalFileSize);

				node.RestoreAsClosedStub(totalFiles, totalFileSize, totalDirectories, totalDirectories);

				return (node, true);
			}

			foreach ((WebDirectory childNode, bool _) in children)
			{
				node.Subdirectories.Add(childNode);
			}

			openDirectories.Add((node, record, children.Count(c => !c.Closed)));

			return (node, false);
		}

		(WebDirectory root, bool _) = Build(rootEntry, null);

		foreach ((WebDirectory node, ScanDatabase.StoredDirectory record, int openChildren) in openDirectories)
		{
			int pendingFileSizeCount = 0;

			foreach (ScanDatabase.MirroredFile file in await scanDatabase.GetFilesByDirectoryIdAsync(record.Id))
			{
				WebFile webFile = new()
				{
					Id = file.Id,
					Url = file.Url,
					FileName = file.FileName,
					FileSize = file.FileSize,
					Description = file.Description
				};

				webFile.AttachTo(node);
				node.Files.Add(webFile);

				if (!skipFileSizeLookups && webFile.FileSize is null)
				{
					result.FilesToRequeueForSize.Add(webFile);
					pendingFileSizeCount++;
				}
			}

			node.ContentFingerprint = node.ComputeContentFingerprint();
			node.PendingWork = openChildren + pendingFileSizeCount;
		}

		return result with { Root = root };
	}

	/// <summary>One directory while rebuilding: its row, its file totals and its children as a linked list (in discovery order).</summary>
	private sealed class ResumeEntry
	{
		public ScanDatabase.StoredDirectory Directory;
		public ScanDatabase.FileAggregate Files;
		public ResumeEntry FirstChild;
		public ResumeEntry NextSibling;
		private ResumeEntry _lastChild;

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
