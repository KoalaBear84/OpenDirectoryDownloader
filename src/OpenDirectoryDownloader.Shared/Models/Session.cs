using Roslyn.Utilities;
using System.Reflection;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Shared.Models;

public class Session
{
	public WebDirectory Root { get; set; }
	public DateTimeOffset Started { get; set; } = DateTimeOffset.MinValue;
	public DateTimeOffset Finished { get; set; } = DateTimeOffset.MinValue;
	public string Version { get; set; } = Assembly.GetEntryAssembly().GetName().Version?.ToString();
	public Dictionary<int, int> HttpStatusCodes { get; set; } = [];
	public Dictionary<string, string> Parameters { get; set; } = [];
	public CommandLineOptions CommandLineOptions { get; set; } = new();
	public List<string> PossibleAlternativeUrls { get; set; } = [];
	public string Description { get; set; }
	public long TotalHttpTraffic { get; set; }
	public int TotalHttpRequests { get; set; }
	public int TotalFiles { get; set; }
	public long TotalFileSizeEstimated { get; set; }
	public int Errors { get; set; }
	[JsonIgnore]
	public int MaxThreads;
	public int Skipped { get; set; }
	public string UploadedUrlsUrl { get; set; }
	public string UploadedUrlsResponse { get; set; }
	public List<string> UrlsWithErrors { get; set; } = [];
	public SpeedtestResult SpeedtestResult { get; set; }

	[JsonIgnore]
	public bool StopLogging { get; set; }
	[JsonIgnore]
	public ConcurrentSet<string> ProcessedUrls { get; set; } = [];
	[JsonIgnore]
	public ConcurrentSet<string> ProcessedBrowserUrls { get; set; } = [];
	[JsonIgnore]
	public bool GDIndex { get; set; }

	/// <summary>
	/// Fast running counters mirroring Root.TotalFiles/TotalFileSize/TotalDirectories, kept up to date
	/// incrementally (see OpenDirectoryIndexer.AddProcessedWebDirectory, WebFileFileSizeProcessor, and the
	/// directory-finished chokepoint) instead of recomputed by walking the whole tree. Used by the periodic
	/// statistics timer, which would otherwise re-walk a tree that only grows larger as a long scan goes on.
	/// Reconciled against the authoritative recursive walk once, at the very end of the scan, so the final
	/// report is never just an accumulation of racy increments.
	/// </summary>
	[JsonIgnore]
	public int RunningTotalFiles { get; set; }

	[JsonIgnore]
	public long RunningTotalFileSize { get; set; }

	[JsonIgnore]
	public int RunningTotalDirectoriesFinished { get; set; }
}
