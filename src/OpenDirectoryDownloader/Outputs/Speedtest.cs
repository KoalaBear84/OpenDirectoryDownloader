using FluentFTP;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;
using System.Diagnostics;

namespace OpenDirectoryDownloader.Outputs;

/// <summary>Measures the download speed on the biggest file found: downloads it for 10 to 25 seconds, until the speed stops improving.</summary>
public static class Speedtest
{
	/// <summary>No data for this long (connecting, waiting for the response, or between two reads) ends the speedtest</summary>
	public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(10);

	public static async Task<SpeedtestResult> HttpAsync(HttpClient httpClient, string url, ILogger logger, int seconds = 25, CancellationToken cancellationToken = default, TimeSpan? stallTimeout = null)
	{
		TimeSpan stall = stallTimeout ?? DefaultStallTimeout;

		logger.LogInformation("Do HTTP speedtest for {url}", url);

		HttpResponseMessage httpResponseMessage = null;

		try
		{
			// Connecting and getting the response headers; reading the data has its own limit in FromStreamAsync
			using (CancellationTokenSource headersSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				headersSource.CancelAfter(stall);

				httpResponseMessage = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headersSource.Token);

				if (!httpResponseMessage.IsSuccessStatusCode || !IsSameUrl(httpResponseMessage, url))
				{
					// Some servers want a referer for their files
					httpResponseMessage.Dispose();

					using HttpRequestMessage request = new(HttpMethod.Get, url);
					request.Headers.Referrer = UrlHelper.GetUrlDirectory(url);
					httpResponseMessage = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersSource.Token);
				}
			}

			if (!httpResponseMessage.IsSuccessStatusCode || !IsSameUrl(httpResponseMessage, url))
			{
				string retrievedUrl = null;

				if (!IsSameUrl(httpResponseMessage, url))
				{
					retrievedUrl = httpResponseMessage.RequestMessage?.RequestUri?.ToString();
				}
				else if (httpResponseMessage.Headers.Location is not null)
				{
					retrievedUrl = httpResponseMessage.Headers.Location.ToString();
				}

				logger.LogWarning("Speedtest cancelled because it returns HTTP {httpStatusCode} (with URL {retrievedUrl})", (int)httpResponseMessage.StatusCode, retrievedUrl);

				return new SpeedtestResult();
			}

			await using Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);

			return await FromStreamAsync(stream, seconds, logger, stall, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Only the stall limit of the headers can get here
			logger.LogWarning("Speedtest cancelled because there was no response within {seconds} seconds", stall.TotalSeconds);

			return new SpeedtestResult();
		}
		finally
		{
			httpResponseMessage?.Dispose();
		}
	}

	/// <summary>
	/// False when the response comes from another URL (a redirect). The URLs are compared normalized: with characters
	/// like 'ç' in the path, a handler can report the same URL percent-encoded, which is not a redirect.
	/// </summary>
	internal static bool IsSameUrl(HttpResponseMessage httpResponseMessage, string url)
	{
		Uri requestUri = httpResponseMessage.RequestMessage?.RequestUri;

		if (requestUri is null)
		{
			return true;
		}

		if (Uri.TryCreate(url, UriKind.Absolute, out Uri expectedUri) && requestUri.IsAbsoluteUri)
		{
			return string.Equals(expectedUri.AbsoluteUri, requestUri.AbsoluteUri, StringComparison.Ordinal);
		}

		return requestUri.OriginalString == url;
	}

	public static async Task<SpeedtestResult> FtpAsync(AsyncFtpClient ftpClient, string url, ILogger logger, int seconds = 25, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Do FTP speedtest for {url}", url);

		await using Stream stream = await ftpClient.OpenRead(new Uri(url).LocalPath, token: cancellationToken);

		return await FromStreamAsync(stream, seconds, logger, DefaultStallTimeout, cancellationToken);
	}

	private static async Task<SpeedtestResult> FromStreamAsync(Stream stream, int seconds, ILogger logger, TimeSpan stallTimeout, CancellationToken cancellationToken)
	{
		// Every read may take at most the stall timeout: a server that stops sending must not hang the speedtest
		using CancellationTokenSource stallSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		int milliseconds = seconds * 1000;

		Stopwatch stopwatch = Stopwatch.StartNew();
		long totalBytesRead = 0;

		byte[] buffer = new byte[16 * Constants.Kilobyte];
		int bytesRead;

		// Bytes downloaded per whole second, and the best second so far
		List<long> bytesPerSecond = [];
		long currentSecond = 0;
		long bytesThisSecond = 0;

		while (true)
		{
			stallSource.CancelAfter(stallTimeout);

			try
			{
				bytesRead = await stream.ReadAsync(buffer, stallSource.Token);
			}
			catch (OperationCanceledException) when (stallSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				Console.WriteLine();
				logger.LogWarning("Speedtest stopped because no data was received for {seconds} seconds", stallTimeout.TotalSeconds);

				break;
			}

			if (bytesRead == 0)
			{
				break;
			}

			long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

			if (elapsedMilliseconds >= milliseconds)
			{
				break;
			}

			long second = elapsedMilliseconds / 1000;

			if (second != currentSecond)
			{
				bytesPerSecond.Add(bytesThisSecond);
				bytesThisSecond = 0;
				currentSecond = second;

				long maxBytesPerSecond = bytesPerSecond.Max();
				ClearCurrentLine();
				Console.Write($"Downloaded: {FileSizeHelper.ToHumanReadable(totalBytesRead)}, Time: {second}s, Speed: {FileSizeHelper.ToHumanReadable(maxBytesPerSecond)}/s ({FileSizeHelper.ToHumanReadable(maxBytesPerSecond * 8, true)}/s)");

				// After 10 seconds: stop when the last 3 seconds were no better than before
				if (elapsedMilliseconds >= 10_000 && bytesPerSecond.Count > 3 && bytesPerSecond[..^3].Max() > bytesPerSecond[^3..].Max())
				{
					break;
				}
			}

			totalBytesRead += bytesRead;
			bytesThisSecond += bytesRead;
		}

		Console.WriteLine();

		stopwatch.Stop();

		SpeedtestResult speedtestResult = new()
		{
			DownloadedBytes = totalBytesRead,
			ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
			MaxBytesPerSecond = bytesPerSecond.Count != 0 ? bytesPerSecond.Max() : bytesThisSecond
		};

		speedtestResult.MaxKBsPerSecond = speedtestResult.MaxBytesPerSecond / 1024d;
		speedtestResult.MaxMBsPerSecond = speedtestResult.MaxBytesPerSecond / 1024d / 1024d;

		if (totalBytesRead > 0)
		{
			logger.LogInformation("Downloaded: {downloadedMBs:F2} MB, Time: {elapsedMilliseconds} ms, Speed: {maxBytesPerSecond}/s ({maxBitsPerSecond}/s)", speedtestResult.DownloadedMBs, speedtestResult.ElapsedMilliseconds, FileSizeHelper.ToHumanReadable(speedtestResult.MaxBytesPerSecond), FileSizeHelper.ToHumanReadable(speedtestResult.MaxBytesPerSecond * 8, true));
		}
		else
		{
			logger.LogWarning("Speedtest failed, nothing downloaded.");
		}

		return speedtestResult;
	}

	private static void ClearCurrentLine()
	{
		try
		{
			if (!Console.IsOutputRedirected)
			{
				Console.Write('\r' + new string(' ', Math.Max(0, Console.WindowWidth - 1)) + '\r');
			}
			else
			{
				Console.WriteLine();
			}
		}
		catch
		{
			// When the console is redirected, and just to be sure
			Console.WriteLine();
		}
	}
}
