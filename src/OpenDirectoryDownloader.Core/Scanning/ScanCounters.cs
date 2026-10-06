namespace OpenDirectoryDownloader.Scanning;

/// <summary>
/// The running numbers of a scan. Every crawl worker updates them, so every update is atomic: the old plain
/// increments lost counts (a 4,681 directory scan reported 4,496 requests).
/// </summary>
public sealed class ScanCounters
{
	private long _httpRequests;
	private long _httpTraffic;
	private long _errors;
	private long _skipped;
	private long _runningFiles;
	private long _runningFileSize;
	private long _directoriesFinished;

	public long HttpRequests => Volatile.Read(ref _httpRequests);

	public long HttpTraffic => Volatile.Read(ref _httpTraffic);

	public long Errors => Volatile.Read(ref _errors);

	public long Skipped => Volatile.Read(ref _skipped);

	/// <summary>Files found so far, kept up to date as directories are added instead of walking the tree.</summary>
	public long RunningFiles => Volatile.Read(ref _runningFiles);

	public long RunningFileSize => Volatile.Read(ref _runningFileSize);

	public long DirectoriesFinished => Volatile.Read(ref _directoriesFinished);

	public void AddHttpRequest() => Interlocked.Increment(ref _httpRequests);

	public void AddHttpTraffic(long bytes) => Interlocked.Add(ref _httpTraffic, bytes);

	public void AddError() => Interlocked.Increment(ref _errors);

	public void AddSkipped() => Interlocked.Increment(ref _skipped);

	public void AddFiles(long count, long size)
	{
		Interlocked.Add(ref _runningFiles, count);
		Interlocked.Add(ref _runningFileSize, size);
	}

	public void AddFileSize(long size) => Interlocked.Add(ref _runningFileSize, size);

	public void AddDirectoryFinished() => Interlocked.Increment(ref _directoriesFinished);

	/// <summary>Restores the counters of an earlier run (--resume) or sets the final, recounted totals.</summary>
	public void Restore(long? httpRequests = null, long? httpTraffic = null, long? errors = null, long? skipped = null, long? runningFiles = null, long? runningFileSize = null, long? directoriesFinished = null)
	{
		if (httpRequests is not null) Volatile.Write(ref _httpRequests, httpRequests.Value);
		if (httpTraffic is not null) Volatile.Write(ref _httpTraffic, httpTraffic.Value);
		if (errors is not null) Volatile.Write(ref _errors, errors.Value);
		if (skipped is not null) Volatile.Write(ref _skipped, skipped.Value);
		if (runningFiles is not null) Volatile.Write(ref _runningFiles, runningFiles.Value);
		if (runningFileSize is not null) Volatile.Write(ref _runningFileSize, runningFileSize.Value);
		if (directoriesFinished is not null) Volatile.Write(ref _directoriesFinished, directoriesFinished.Value);
	}
}
