using OpenDirectoryDownloader.Collections;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Threading;
using System.Reflection;

namespace OpenDirectoryDownloader.Scanning;

/// <summary>
/// Everything one scan knows and counts, shared by the crawler, the parsers and the outputs. One instance per scan
/// (so per host, and per test), instead of the static session the crawler used to keep.
/// </summary>
public sealed class ScanSession
{
	public ScanSession(WebDirectory root, int threads = 1)
	{
		Root = root;
		Concurrency = new ConcurrencyGovernor(threads);
	}

	public WebDirectory Root { get; set; }

	public DateTimeOffset Started { get; set; } = DateTimeOffset.MinValue;

	public DateTimeOffset Finished { get; set; } = DateTimeOffset.MinValue;

	public string Version { get; set; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

	/// <summary>Server information for the report, like the FTP banner.</summary>
	public string Description { get; set; }

	public ScanCounters Counters { get; } = new();

	public StatusCodeCounter HttpStatusCodes { get; } = new();

	public ConcurrentStringSet UrlsWithErrors { get; } = new();

	public RateLimitTracker RateLimit { get; } = new();

	public ConcurrentStringSet PossibleAlternativeUrls { get; } = new();

	/// <summary>Pages that were already fetched through the browser, so it is tried once per URL.</summary>
	public ConcurrentStringSet BrowserAttempts { get; } = new();

	public ProcessedUrlSet ProcessedUrls { get; set; } = new();

	/// <summary>State a site parser keeps between the directories of a scan (detected index type, tokens, ...).</summary>
	public SiteStateStore SiteState { get; } = new();

	/// <summary>How many directories may be processed at once, lowered by sites that need it (Google Drive indexes, FTP connection limits, ...).</summary>
	public ConcurrencyGovernor Concurrency { get; }

	/// <summary>Final totals, filled in once the scan is finished.</summary>
	public long TotalFiles { get; set; }

	public long TotalFileSizeEstimated { get; set; }

	public string UploadedUrlsUrl { get; set; }

	/// <summary>The URLs file was to be uploaded (-l), but no site took it</summary>
	public bool UploadFailed { get; set; }

	public string UploadedUrlsResponse { get; set; }

	public SpeedtestResult SpeedtestResult { get; set; }

	/// <summary>Set while the scan is knowingly waiting a long time (a site's rate limit), so the periodic statistics stay quiet.</summary>
	public bool StatisticsSuppressed { get; set; }

	/// <summary>Counts an error for <paramref name="url"/> and remembers the URL for the report.</summary>
	public void ReportError(string url)
	{
		Counters.AddError();
		UrlsWithErrors.Add(url);
	}
}
