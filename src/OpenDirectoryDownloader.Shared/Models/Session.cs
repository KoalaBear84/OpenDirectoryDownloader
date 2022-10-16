using Newtonsoft.Json;
using Roslyn.Utilities;
using System.Reflection;

namespace OpenDirectoryDownloader.Shared.Models;

public class Session
{
	public WebDirectory Root { get; set; }
	public DateTimeOffset Started { get; set; } = DateTimeOffset.MinValue;
	public DateTimeOffset Finished { get; set; } = DateTimeOffset.MinValue;
	public string Version { get; set; } = Assembly.GetEntryAssembly().GetName().Version?.ToString();
	public Dictionary<int, int> HttpStatusCodes { get; set; } = new();
	public Dictionary<string, string> Parameters { get; set; } = new();
	public CommandLineOptions CommandLineOptions { get; set; } = new();
	public List<string> PossibleAlternativeUrls { get; set; } = new();
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
	public List<string> UrlsWithErrors { get; set; } = new();
	public SpeedtestResult SpeedtestResult { get; set; }

	[JsonIgnore]
	public bool StopLogging { get; set; }
	[JsonIgnore]
	public ConcurrentSet<string> ProcessedUrls { get; set; } = new();
	[JsonIgnore]
	public bool GDIndex { get; set; }
}
