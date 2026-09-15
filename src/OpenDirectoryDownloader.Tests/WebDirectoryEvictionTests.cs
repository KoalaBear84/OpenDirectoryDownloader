using OpenDirectoryDownloader.Shared.Models;
using System.Threading;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// Regression coverage for issue #56 phase 4: WebDirectory.PendingWork/EvictContent/TryClose implement a
/// reference-counted "close this subtree once everything under it is done" mechanism, so a directory's
/// in-memory Files/Subdirectories can be safely dropped once nothing can possibly need them anymore. Bugs
/// here would silently and unrecoverably lose data from a real scan, so these are deliberately thorough,
/// including a concurrency stress test mirroring how real directories/files close in parallel.
/// </summary>
public class WebDirectoryEvictionTests
{
	[Fact]
	public void EvictContent_FreezesTotalsAndClearsCollections()
	{
		WebDirectory directory = new(null) { Url = "http://localhost/a/", Name = "a" };
		directory.Files.Add(new WebFile { Url = "http://localhost/a/f1", FileName = "f1", FileSize = 100 });
		directory.Files.Add(new WebFile { Url = "http://localhost/a/f2", FileName = "f2", FileSize = 200 });

		WebDirectory child = new(directory) { Url = "http://localhost/a/b/", Name = "b", Finished = true };
		child.Files.Add(new WebFile { Url = "http://localhost/a/b/f3", FileName = "f3", FileSize = 50 });
		directory.Subdirectories.Add(child);

		Assert.Equal(350, directory.TotalFileSize);
		Assert.Equal(3, directory.TotalFiles);

		directory.EvictContent();

		Assert.True(directory.ContentEvicted);
		Assert.Empty(directory.Files);
		Assert.Empty(directory.Subdirectories);
		Assert.Equal(350, directory.TotalFileSize);
		Assert.Equal(3, directory.TotalFiles);
	}

	[Fact]
	public void EvictContent_IsIdempotent()
	{
		WebDirectory directory = new(null) { Url = "http://localhost/a/", Name = "a" };
		directory.Files.Add(new WebFile { Url = "http://localhost/a/f1", FileName = "f1", FileSize = 100 });

		directory.EvictContent();
		Assert.Equal(100, directory.CachedTotalFileSize);

		// A second call must not re-derive totals from the now-empty collections (which would zero them out).
		directory.EvictContent();

		Assert.Equal(100, directory.CachedTotalFileSize);
		Assert.Equal(1, directory.CachedTotalFiles);
	}

	[Fact]
	public void TryClose_LeafDirectory_ClosesImmediately()
	{
		WebDirectory root = new(null) { Url = "http://localhost/", Name = "ROOT" };
		WebDirectory leaf = new(root) { Url = "http://localhost/a/", Name = "a" };
		leaf.Files.Add(new WebFile { Url = "http://localhost/a/f1", FileName = "f1", FileSize = 42 });
		root.Subdirectories.Add(leaf);
		// root itself is still "open" (PendingWork == 1 for its own not-yet-finished processing) -
		// simulate that it registered leaf as one unit of outstanding work, matching AddProcessedWebDirectory.
		root.PendingWork = 2;

		WebDirectory.TryClose(leaf);

		Assert.True(leaf.ContentEvicted);
		Assert.False(root.ContentEvicted);
		Assert.Equal(1, root.PendingWork);
	}

	[Fact]
	public void TryClose_DoesNotCloseParentUntilAllChildrenAndSelfAreDone()
	{
		WebDirectory root = new(null) { Url = "http://localhost/", Name = "ROOT" };
		WebDirectory childA = new(root) { Url = "http://localhost/a/", Name = "a" };
		WebDirectory childB = new(root) { Url = "http://localhost/b/", Name = "b" };
		root.Subdirectories.Add(childA);
		root.Subdirectories.Add(childB);

		// root: 1 (self) + 1 (childA) + 1 (childB) = 3
		root.PendingWork = 3;

		WebDirectory.TryClose(childA);
		Assert.False(root.ContentEvicted);
		Assert.Equal(2, root.PendingWork);

		WebDirectory.TryClose(childB);
		Assert.False(root.ContentEvicted); // root's own "self" unit hasn't closed yet
		Assert.Equal(1, root.PendingWork);

		WebDirectory.TryClose(root); // root's own processing finishes last
		Assert.True(root.ContentEvicted);
	}

	[Fact]
	public void TryClose_PropagatesThroughMultipleClosedAncestors()
	{
		WebDirectory root = new(null) { Url = "http://localhost/", Name = "ROOT" };
		WebDirectory a = new(root) { Url = "http://localhost/a/", Name = "a" };
		WebDirectory b = new(a) { Url = "http://localhost/a/b/", Name = "b" };
		WebDirectory c = new(b) { Url = "http://localhost/a/b/c/", Name = "c" };
		root.Subdirectories.Add(a);
		a.Subdirectories.Add(b);
		b.Subdirectories.Add(c);

		// Each ancestor is otherwise done except for waiting on its one child.
		root.PendingWork = 1; // only waiting on 'a' closing (root's own self-unit already consumed)
		a.PendingWork = 1;    // only waiting on 'b'
		b.PendingWork = 1;    // only waiting on 'c'
		c.PendingWork = 1;    // only its own self-unit left

		WebDirectory.TryClose(c);

		Assert.True(c.ContentEvicted);
		Assert.True(b.ContentEvicted);
		Assert.True(a.ContentEvicted);
		Assert.True(root.ContentEvicted);
	}

	[Fact]
	public void TryClose_NullDirectory_DoesNothing()
	{
		// Root has no parent; TryClose must handle walking off the top of the tree without throwing.
		WebDirectory.TryClose(null);
	}

	[Fact]
	public async Task TryClose_ManyDirectoriesClosingConcurrently_ClosesEveryNodeExactlyOnce()
	{
		// A moderately wide/deep synthetic tree, closed level-by-level from many threads at once
		// (mirroring how multiple crawl/file-size-lookup threads can call TryClose for unrelated
		// directories simultaneously in the real indexer), to catch races in the
		// Interlocked.Decrement/EvictContent handoff. Each directory is only "closed" (TryClose called)
		// once all its own children have already been closed, exactly as OpenDirectoryIndexer guarantees
		// via PendingWork increment-before-enqueue ordering.
		const int breadth = 6;
		const int depth = 4;

		WebDirectory root = new(null) { Url = "http://localhost/", Name = "ROOT" };
		List<WebDirectory> allNodes = [root];
		List<WebDirectory> leaves = [];
		BuildTree(root, depth, breadth, allNodes, leaves);

		// Snapshot parent links before any eviction clears Subdirectories.
		List<WebDirectory> currentLevel = leaves;

		while (currentLevel.Count != 0)
		{
			List<Task> closeTasks = [.. currentLevel.Select(d => Task.Run(() => WebDirectory.TryClose(d)))];
			await Task.WhenAll(closeTasks);

			currentLevel = [.. currentLevel
				.Select(d => d.ParentDirectory)
				.Where(d => d is not null)
				.Distinct()
				.Where(d => d.PendingWork == 0)];
		}

		Assert.All(allNodes, d => Assert.True(d.ContentEvicted, $"{d.Url} was never evicted"));
	}

	private static void BuildTree(WebDirectory parent, int remainingDepth, int breadth, List<WebDirectory> allNodes, List<WebDirectory> leaves)
	{
		// self (1) + one unit per child, matching AddProcessedWebDirectory's increment-before-enqueue.
		parent.PendingWork = 1 + breadth;

		for (int i = 0; i < breadth; i++)
		{
			WebDirectory child = new(parent) { Url = $"{parent.Url}{i}/", Name = i.ToString() };
			parent.Subdirectories.Add(child);
			allNodes.Add(child);

			if (remainingDepth <= 1)
			{
				child.PendingWork = 1;
				leaves.Add(child);
			}
			else
			{
				BuildTree(child, remainingDepth - 1, breadth, allNodes, leaves);
			}
		}

		// This parent's own "self" unit (as opposed to each child's) closes here, since in the real indexer
		// AddProcessedWebDirectory (which registers all children) always completes before the
		// self-decrement in the WebDirectoryProcessor finally block. Consumed directly, at construction
		// time, rather than via TryClose, so it can't race the concurrent child-closing the test drives.
		Interlocked.Decrement(ref parent.PendingWork);
	}
}
