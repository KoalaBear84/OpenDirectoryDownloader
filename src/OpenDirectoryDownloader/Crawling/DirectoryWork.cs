using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.Crawling;

/// <summary>One directory being processed by one crawl worker.</summary>
public sealed class DirectoryWork(WebDirectory directory, string workerName, Action<DirectoryWork, long?> convertToFile)
{
	public WebDirectory Directory { get; } = directory;

	/// <summary>For the log: which worker (P01, P02, ...)</summary>
	public string WorkerName { get; } = workerName;

	public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;

	/// <summary>Set when it turned out to be a file: it is not a directory to finish anymore</summary>
	public bool BecameFile { get; private set; }

	/// <summary>Moves the directory's time limit, when the server asked to wait (Retry-After)</summary>
	public Action<TimeSpan> ExtendTimeout { get; set; }

	/// <summary>It is a file (no HTML, or too big for a listing): its parent lists it as a file instead.</summary>
	public void ConvertToFile(long? fileSize)
	{
		BecameFile = true;
		convertToFile(this, fileSize);
	}
}

/// <summary>The crawler gave up on a directory (after retries, or on an error that is final).</summary>
public sealed class DirectoryAbandonedException(Exception innerException) : Exception(innerException?.Message, innerException);
