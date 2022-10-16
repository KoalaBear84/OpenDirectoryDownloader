using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared.Models;
using System.Reflection;
using System.Text;

namespace OpenDirectoryDownloader;

public static class Statistics
{
	private static readonly string VersionNumber = Assembly.GetExecutingAssembly().GetName().Version.ToString();

	public static Dictionary<string, ExtensionStats> GetExtensions(WebDirectory webDirectory)
	{
		Dictionary<string, ExtensionStats> extensionCount;

		extensionCount = webDirectory.Files
			.GroupBy(f => Path.GetExtension(f.FileName).ToLowerInvariant(), f => f)
			.ToDictionary(f => f.Key.ToLowerInvariant(), f => new ExtensionStats { Count = f.Count(), FileSize = f.ToList().Sum(f2 => f2.FileSize) });

		foreach (WebDirectory subdirectory in webDirectory.Subdirectories)
		{
			Dictionary<string, ExtensionStats> subdirectoryExtensions = GetExtensions(subdirectory);

			foreach (KeyValuePair<string, ExtensionStats> subdirectoryExtension in subdirectoryExtensions)
			{
				if (!extensionCount.ContainsKey(subdirectoryExtension.Key))
				{
					extensionCount.Add(subdirectoryExtension.Key, new ExtensionStats());
				}

				extensionCount[subdirectoryExtension.Key].Count += subdirectoryExtension.Value.Count;
				extensionCount[subdirectoryExtension.Key].FileSize += subdirectoryExtension.Value.FileSize;
			}
		}

		return extensionCount;
	}

	public static string GetSessionStats(Session session, bool includeExtensions = false, bool includeFullExtensions = false, bool onlyRedditStats = false, bool includeBanner = false)
	{
		Dictionary<string, ExtensionStats> extensionsStats = new();

		if (includeExtensions || includeFullExtensions)
		{
			extensionsStats = GetExtensions(session.Root);
		}

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

		foreach (KeyValuePair<int, int> statusCode in session.HttpStatusCodes.OrderBy(statusCode => statusCode.Key))
		{
			stringBuilder.AppendLine($"{statusCode.Key}: {statusCode.Value}");
		}

		stringBuilder.AppendLine($"Total files: {Library.FormatWithThousands(session.Root.TotalFiles)}, Total estimated size: {(session.Root.TotalFileSize > 0 ? FileSizeHelper.ToHumanReadable(session.Root.TotalFileSize) : "n/a")}");
		stringBuilder.AppendLine($"Total directories: {Library.FormatWithThousands(session.Root.TotalDirectories + 1)}");
		stringBuilder.AppendLine($"Total HTTP requests: {Library.FormatWithThousands(session.TotalHttpRequests)}, Total HTTP traffic: {FileSizeHelper.ToHumanReadable(session.TotalHttpTraffic)}");

		if (onlyRedditStats)
		{
			stringBuilder.Clear();
		}

		string uploadedUrlsText = !string.IsNullOrWhiteSpace(session.UploadedUrlsUrl) ? $"[Urls file]({session.UploadedUrlsUrl})" : string.Empty;

		if (session.Root.Url.Length < 40)
		{
			stringBuilder.AppendLine($"|**Url:** [{session.Root.Url}]({session.Root.Url})||{uploadedUrlsText}|");
		}
		else
		{
			stringBuilder.AppendLine($"|**Url:** {$"[{session.Root.Url[..38]}...]({session.Root.Url})"}||{uploadedUrlsText}|");
		}

		stringBuilder.AppendLine("|:-|-:|-:|");

		if (includeExtensions)
		{
			stringBuilder.AppendLine("|**Extension (Top 5)**|**Files**|**Size**|");

			foreach (KeyValuePair<string, ExtensionStats> extensionStat in extensionsStats.OrderByDescending(e =>
			{
				if (e.Value.FileSize > 0)
				{
					return e.Value.FileSize;
				}
				else
				{
					return e.Value.Count;
				}
			}).Take(5))
			{
				stringBuilder.AppendLine($"|{extensionStat.Key}|{Library.FormatWithThousands(extensionStat.Value.Count)}|{(extensionStat.Value.FileSize > 0 ? FileSizeHelper.ToHumanReadable(extensionStat.Value.FileSize) : "n/a")}|");
			}

			stringBuilder.AppendLine($"|**Dirs:** {Library.FormatWithThousands(session.Root.TotalDirectories + 1)} **Ext:** {Library.FormatWithThousands(extensionsStats.Count)}|**Total:** {Library.FormatWithThousands(session.TotalFiles)}|**Total:** {(session.TotalFileSizeEstimated > 0 ? FileSizeHelper.ToHumanReadable(session.TotalFileSizeEstimated) : "n/a")}|");
		}

		// stringBuilder.AppendLine($"|**Date (UTC):** {session.Started.ToString(Constants.DateTimeFormat)}|**Time:** {TimeSpan.FromSeconds((int)((session.Finished == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : session.Finished) - session.Started).TotalSeconds)}|{(session.SpeedtestResult != null ? $"**Speed:** {(session.SpeedtestResult.DownloadedBytes > 0 ? $"{session.SpeedtestResult.MaxMBsPerSecond:F1} MB/s ({session.SpeedtestResult.MaxMBsPerSecond * 8:F0} mbit)" : "Failed")}" : string.Empty)}|");

		stringBuilder.AppendLine($"|**Date (UTC):** {session.Started.ToString(Constants.DateTimeFormat)}|**Time:** {TimeSpan.FromSeconds((int)((session.Finished == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : session.Finished) - session.Started).TotalSeconds)}|{(session.SpeedtestResult != null ? $"**Speed:** {(session.SpeedtestResult.DownloadedBytes > 0 ? $"{session.SpeedtestResult.MaxBytesPerSecond / (double)Constants.Megabyte:F2} MB/s ({session.SpeedtestResult.MaxBytesPerSecond / (double)Constants.Megabyte * 8:F1} mbit)" : "Failed")}" : string.Empty)}|");

		if (onlyRedditStats || includeBanner)
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine($"^(Created by [KoalaBear84's OpenDirectory Indexer v{VersionNumber}](https://github.com/KoalaBear84/OpenDirectoryDownloader/))");
		}

		return stringBuilder.ToString();
	}
}
