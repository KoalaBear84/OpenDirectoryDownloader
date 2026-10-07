using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;

namespace OpenDirectoryDownloader.Outputs;

/// <summary>Saves the session as JSON (format 2), at the end of a scan (-j) or any time with the J key.</summary>
public sealed class SessionJsonFactory(ScanSession session, ScanDatabase scanDatabase, CliInvocation invocation, OutputPaths outputPaths, ILogger<SessionJsonFactory> logger)
{
	/// <param name="totalDirectories">Finished directories below the root, when the scan is finished</param>
	public async Task SaveAsync(long? totalDirectories = null, CancellationToken cancellationToken = default)
	{
		string jsonPath = outputPaths.Get("json");

		if (File.Exists(jsonPath))
		{
			logger.LogInformation("Session JSON file already exists, not overwriting: {path}", jsonPath);
			Console.WriteLine($"Session JSON file already exists, not overwriting: {jsonPath}");
			return;
		}

		logger.LogInformation("Saving session to JSON..");
		Console.WriteLine("Saving session to JSON..");

		await SessionJsonWriter.WriteAsync(jsonPath, CreateHeader(totalDirectories), scanDatabase, session.Root.Url, cancellationToken);

		logger.LogInformation("Saved session: {path}", jsonPath);
		Console.WriteLine($"Saved session: {jsonPath}");
	}

	public SessionHeader CreateHeader(long? totalDirectories)
	{
		SessionHeader header = new()
		{
			Scan = new SessionScanInfo
			{
				RootUrl = session.Root.Url,
				Started = session.Started,
				Finished = session.Finished == DateTimeOffset.MinValue ? null : session.Finished,
				AppVersion = session.Version,
				Description = session.Description,
				Options = GetOptions()
			},
			Stats = new SessionStats
			{
				TotalFiles = session.Finished == DateTimeOffset.MinValue ? session.Counters.RunningFiles : session.TotalFiles,
				TotalFileSize = session.Finished == DateTimeOffset.MinValue ? session.Counters.RunningFileSize : session.TotalFileSizeEstimated,
				TotalDirectories = totalDirectories ?? session.Counters.DirectoriesFinished,
				HttpRequests = session.Counters.HttpRequests,
				HttpTraffic = session.Counters.HttpTraffic,
				Errors = session.Counters.Errors,
				Skipped = session.Counters.Skipped,
				HttpStatusCodes = new Dictionary<int, int>(session.HttpStatusCodes.Snapshot()),
				UploadedUrlsUrl = session.UploadedUrlsUrl,
				Speedtest = session.SpeedtestResult is { } speedtest ? new SessionSpeedtest
				{
					DownloadedBytes = speedtest.DownloadedBytes,
					ElapsedMilliseconds = speedtest.ElapsedMilliseconds,
					MaxBytesPerSecond = speedtest.MaxBytesPerSecond
				} : null
			},
			UrlsWithErrors = [.. session.UrlsWithErrors.Order(NaturalSortStringComparer.InvariantCulture)],
			PossibleAlternativeUrls = [.. session.PossibleAlternativeUrls]
		};

		return header;
	}

	/// <summary>The options the scan ran with that say something about the result. No passwords, tokens, proxies or header values.</summary>
	private Dictionary<string, string> GetOptions()
	{
		Dictionary<string, string> options = new()
		{
			["threads"] = invocation.Crawl.Threads.ToString(),
			["timeout"] = invocation.Crawl.TimeoutSeconds.ToString()
		};

		if (invocation.Crawl.WaitSecondsBetweenCalls > 0)
		{
			options["wait"] = invocation.Crawl.WaitSecondsBetweenCalls.ToString();
		}

		if (invocation.Crawl.FastScan)
		{
			options["fast-scan"] = "true";
		}

		if (invocation.Crawl.ExactFileSizes)
		{
			options["exact-file-sizes"] = "true";
		}

		if (!string.IsNullOrWhiteSpace(invocation.Http.UserAgent))
		{
			options["user-agent"] = invocation.Http.UserAgent;
		}

		if (!string.IsNullOrWhiteSpace(invocation.Http.HttpCloakPreset))
		{
			options["http-cloak"] = invocation.Http.HttpCloakPreset;
		}

		if (invocation.Http.Headers.Count > 0)
		{
			options["header"] = string.Join(", ", invocation.Http.Headers.Select(header => header.Split(':')[0].Trim()));
		}

		return options;
	}
}
