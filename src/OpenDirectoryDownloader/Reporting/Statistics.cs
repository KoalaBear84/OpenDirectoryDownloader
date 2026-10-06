using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using OpenDirectoryDownloader.Urls;
using System.Reflection;
using System.Text;

namespace OpenDirectoryDownloader.Reporting;

/// <summary>The numbers of a whole scan that need the files themselves (per extension, totals), read once from the scan database.</summary>
public sealed record ScanReport(Dictionary<string, ExtensionStats> Extensions)
{
	public static async Task<ScanReport> CreateAsync(ScanDatabase scanDatabase)
	{
		return new ScanReport(await Statistics.GetExtensionsAsync(scanDatabase));
	}
}

/// <summary>Formats the statistics shown during and after a scan, and the Reddit markdown table.</summary>
public static class Statistics
{
	private static readonly string VersionNumber = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

	/// <summary>Count and size per lower case extension (with the dot, "" for none).</summary>
	public static Task<Dictionary<string, ExtensionStats>> GetExtensionsAsync(ScanDatabase scanDatabase) => scanDatabase.GetExtensionStatsAsync();

	/// <param name="report">The per extension numbers; needed for <paramref name="includeExtensions"/> or <paramref name="includeFullExtensions"/></param>
	/// <param name="totalDirectories">Finished directories below the root, when known; the running counter otherwise</param>
	public static string GetSessionStats(ScanSession session, ScanReport report = null, bool includeExtensions = false, bool includeFullExtensions = false, bool onlyRedditStats = false, bool includeBanner = false, long? totalDirectories = null)
	{
		Dictionary<string, ExtensionStats> extensionsStats = (includeExtensions || includeFullExtensions) ? report?.Extensions ?? [] : [];

		StringBuilder stringBuilder = new();

		if (includeFullExtensions)
		{
			stringBuilder.AppendLine($"Extensions");

			foreach (KeyValuePair<string, ExtensionStats> extensionStat in extensionsStats.OrderByDescending(e => e.Value.Count).Take(50))
			{
				stringBuilder.AppendLine($"{extensionStat.Key}: {extensionStat.Value.Count} files, {extensionStat.Value.FileSize} bytes");
			}
		}

		stringBuilder.AppendLine($"Http status codes");

		foreach (KeyValuePair<int, int> statusCode in session.HttpStatusCodes.Snapshot())
		{
			stringBuilder.AppendLine($"{statusCode.Key}: {statusCode.Value}");
		}

		// Running counters, not a walk of the tree: this is printed periodically during the whole scan
		stringBuilder.AppendLine($"Total files: {UrlHelper.FormatWithThousands(session.Counters.RunningFiles)}, Total estimated size: {(session.Counters.RunningFileSize > 0 ? FileSizeHelper.ToHumanReadable(session.Counters.RunningFileSize) : "n/a")}");
		stringBuilder.AppendLine($"Total directories: {UrlHelper.FormatWithThousands(session.Counters.DirectoriesFinished + 1)}");
		stringBuilder.AppendLine($"Total HTTP requests: {UrlHelper.FormatWithThousands(session.Counters.HttpRequests)}, Total HTTP traffic: {FileSizeHelper.ToHumanReadable(session.Counters.HttpTraffic)}");

		if (onlyRedditStats)
		{
			stringBuilder.Clear();
		}

		string uploadedUrlsText = !string.IsNullOrWhiteSpace(session.UploadedUrlsUrl) ? $"[Urls file]({session.UploadedUrlsUrl})" : string.Empty;
		string rootUrl = session.Root.Url;

		if (rootUrl.Length < 40)
		{
			stringBuilder.AppendLine($"|**Url:** [{rootUrl}]({rootUrl})||{uploadedUrlsText}|");
		}
		else
		{
			stringBuilder.AppendLine($"|**Url:** [{rootUrl[..38]}...]({rootUrl})||{uploadedUrlsText}|");
		}

		stringBuilder.AppendLine("|:-|-:|-:|");

		if (includeExtensions)
		{
			stringBuilder.AppendLine("|**Extension (Top 5)**|**Files**|**Size**|");

			foreach (KeyValuePair<string, ExtensionStats> extensionStat in extensionsStats.OrderByDescending(e => e.Value.FileSize > 0 ? e.Value.FileSize : e.Value.Count).Take(5))
			{
				stringBuilder.AppendLine($"|{extensionStat.Key}|{UrlHelper.FormatWithThousands(extensionStat.Value.Count)}|{(extensionStat.Value.FileSize > 0 ? FileSizeHelper.ToHumanReadable(extensionStat.Value.FileSize) : "n/a")}|");
			}

			long directories = totalDirectories ?? session.Root.TotalDirectories;

			stringBuilder.AppendLine($"|**Dirs:** {UrlHelper.FormatWithThousands(directories + 1)} **Ext:** {UrlHelper.FormatWithThousands(extensionsStats.Count)}|**Total:** {UrlHelper.FormatWithThousands(session.TotalFiles)}|**Total:** {(session.TotalFileSizeEstimated > 0 ? FileSizeHelper.ToHumanReadable(session.TotalFileSizeEstimated) : "n/a")}|");
		}

		SpeedtestResult speedtestResult = session.SpeedtestResult;
		string speed = speedtestResult is null ? string.Empty :
			$"**Speed:** {(speedtestResult.DownloadedBytes > 0 ? $"{speedtestResult.MaxBytesPerSecond / (double)Constants.Megabyte:F2} MB/s ({speedtestResult.MaxBytesPerSecond / (double)Constants.Megabyte * 8:F1} mbit)" : "Failed")}";

		DateTimeOffset finished = session.Finished == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : session.Finished;

		stringBuilder.AppendLine($"|**Date (UTC):** {session.Started.ToString(Constants.DateTimeFormat)}|**Time:** {TimeSpan.FromSeconds((int)(finished - session.Started).TotalSeconds)}|{speed}|");

		if (!onlyRedditStats && !includeBanner)
		{
			return stringBuilder.ToString();
		}

		stringBuilder.AppendLine();
		stringBuilder.AppendLine($"^(Created by [KoalaBear84's OpenDirectory Indexer v{VersionNumber}](https://github.com/KoalaBear84/OpenDirectoryDownloader/))");

		return stringBuilder.ToString();
	}
}
