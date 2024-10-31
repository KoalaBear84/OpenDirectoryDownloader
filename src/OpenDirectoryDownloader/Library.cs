using AngleSharp.Dom;
using FluentFTP;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader;

public class Library
{
	public static string GetCurrentWorkingDirectory()
	{
		string cwd = Directory.GetCurrentDirectory();

		if (!cwd.EndsWith(Path.DirectorySeparatorChar.ToString()))
		{
			cwd += Path.DirectorySeparatorChar;
		}

		return cwd;
	}

	public static string GetScansPath()
	{
		string scansPath = $"{GetCurrentWorkingDirectory()}Scans";

		if (!Directory.Exists(scansPath))
		{
			Directory.CreateDirectory(scansPath);
		}

		return scansPath;
	}

	public static string GetOutputFullPath(Session session, OpenDirectoryIndexerSettings openDirectoryIndexerSettings, string extension)
	{
		string fileName = openDirectoryIndexerSettings.CommandLineOptions.OutputFile is not null ? $"{openDirectoryIndexerSettings.CommandLineOptions.OutputFile}.{extension}" : $"{CleanUriToFilename(session.Root.Uri)}.{extension}";

		string path;

		if (Path.IsPathFullyQualified(fileName))
		{
			path = fileName;
		}
		else
		{
			string scansPath = GetScansPath();
			path = Path.Combine(scansPath, fileName);
		}

		return path;
	}

	public static bool IsBase64String(string base64)
	{
		Span<byte> buffer = new(new byte[base64.Length]);
		return Convert.TryFromBase64String(base64, buffer, out _);
	}

	public static string FixUrl(string url)
	{
		url = url.Trim();

		if (IsBase64String(url))
		{
			byte[] data = Convert.FromBase64String(url);
			url = Encoding.UTF8.GetString(data);
		}

		if (!url.Contains("http:") && !url.Contains("https:") && !url.Contains("ftp:") && !url.Contains("ftps:"))
		{
			url = $"http://{url}";
		}

		Uri uri = new(url);

		if (!url.EndsWith('/') && string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(uri.AbsolutePath))) && string.IsNullOrWhiteSpace(uri.Query))
		{
			url += "/";
		}

		if (uri.Host == Constants.GoogleDriveDomain)
		{
			UrlEncodingParser urlEncodingParser = new(url);

			if (urlEncodingParser.AllKeys.Contains("usp"))
			{
				urlEncodingParser.Remove("usp");
			}

			url = urlEncodingParser.ToString();
		}

		return url;
	}

	public static void SaveSessionJson(Session session, string filePath)
	{
		JsonSerializer jsonSerializer = new();

		using StreamWriter streamWriter = new(filePath);
		using JsonWriter jsonWriter = new JsonTextWriter(streamWriter);

		jsonSerializer.Serialize(jsonWriter, session);
	}

	public static string CleanUriToFilename(Uri uri)
	{
		return PathHelper.GetValidPath(Uri.UnescapeDataString(uri.ToString()));
	}

	public static Session LoadSessionJson(string fileName)
	{
		using StreamReader streamReader = new(fileName);
		using JsonReader jsonReader = new JsonTextReader(streamReader);

		return new JsonSerializer().Deserialize<Session>(jsonReader);
	}

	public static string FormatWithThousands(object value)
	{
		return $"{value:#,0}";
	}

	private static long GetSpeedInBytes(IGrouping<long, KeyValuePair<long, long>> measurements, int useMiliseconds = 0)
	{
		long time = useMiliseconds == 0 ? measurements.Last().Key - measurements.First().Key : useMiliseconds;
		long downloadedBytes = measurements.Last().Value - measurements.First().Value;
		return downloadedBytes / (time / 1000);
	}

	public static async Task<SpeedtestResult> DoSpeedTestHttpAsync(HttpClient httpClient, string url, int seconds = 25)
	{
		Program.Logger.Information("Do HTTP speedtest for {url}", url);

		HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

		if (!httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.RequestMessage?.RequestUri?.ToString() != url)
		{
			httpClient.DefaultRequestHeaders.Referrer = GetUrlDirectory(url);
			httpResponseMessage.Dispose();
			httpResponseMessage = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		}

		if (!httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.RequestMessage?.RequestUri?.OriginalString != url)
		{
			string retrievedUrl = null;

			if (httpResponseMessage.RequestMessage?.RequestUri?.OriginalString != url)
			{
				retrievedUrl = httpResponseMessage.RequestMessage?.RequestUri?.ToString();
			}
			else if (httpResponseMessage.Headers.Location is not null)
			{
				retrievedUrl = httpResponseMessage.Headers.Location.ToString();
			}

			Program.Logger.Warning("Speedtest cancelled because it returns HTTP {httpStatusCode} (with URL {retrievedUrl}", (int)httpResponseMessage.StatusCode, retrievedUrl);
			return new SpeedtestResult();
		}

		try
		{
			using Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync();

			SpeedtestResult speedtestResult = SpeedtestFromStream(stream, seconds);

			return speedtestResult;
		}
		finally
		{
			httpResponseMessage.Dispose();
		}
	}

	public static async Task<SpeedtestResult> DoSpeedTestFtpAsync(AsyncFtpClient ftpClient, string url, int seconds = 25)
	{
		Program.Logger.Information("Do FTP speedtest for {url}", url);

		Uri uri = new(url);

		await using Stream stream = await ftpClient.OpenRead(uri.LocalPath);

		SpeedtestResult speedtestResult = SpeedtestFromStream(stream, seconds);

		return await Task.FromResult(speedtestResult);
	}

	private static SpeedtestResult SpeedtestFromStream(Stream stream, int seconds)
	{
		int miliseconds = seconds * 1000;

		Stopwatch stopwatch = Stopwatch.StartNew();
		long totalBytesRead = 0;

		byte[] buffer = new byte[2048];
		int bytesRead;

		List<KeyValuePair<long, long>> measurements = new(10_000);
		long previousTime = 0;

		while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			if (stopwatch.ElapsedMilliseconds >= miliseconds)
			{
				break;
			}

			if (previousTime / 1000 < stopwatch.ElapsedMilliseconds / 1000)
			{
				ClearCurrentLine();
				long maxBytesPerSecond = measurements.Count != 0 ? measurements.GroupBy(m => m.Key / 1000).Max(s => GetSpeedInBytes(s, 1000)) : 0;
				Console.Write($"Downloaded: {FileSizeHelper.ToHumanReadable(totalBytesRead)}, Time: {stopwatch.ElapsedMilliseconds / 1000}s, Speed: {FileSizeHelper.ToHumanReadable(maxBytesPerSecond):F1)}/s ({FileSizeHelper.ToHumanReadable(maxBytesPerSecond * 8, true):F0}/s)");
			}

			if (stopwatch.ElapsedMilliseconds >= 10_000)
			{
				// Second changed
				if (previousTime / 1000 < stopwatch.ElapsedMilliseconds / 1000)
				{
					List<IGrouping<long, KeyValuePair<long, long>>> perSecond = measurements.GroupBy(m => m.Key / 1000).ToList();

					if (!perSecond.Any())
					{
						break;
					}

					double maxSpeedLastSeconds = perSecond.TakeLast(3).Max(s => GetSpeedInBytes(s, 1000));
					double maxSpeedBefore = perSecond.Take(perSecond.Count - 3).Max(s => GetSpeedInBytes(s, 1000));

					// If no improvement in speed
					if (maxSpeedBefore > maxSpeedLastSeconds)
					{
						break;
					}
				}
			}

			totalBytesRead += bytesRead;

			measurements.Add(new KeyValuePair<long, long>(stopwatch.ElapsedMilliseconds, totalBytesRead));
			previousTime = stopwatch.ElapsedMilliseconds;
		}

		Console.WriteLine();

		stopwatch.Stop();

		SpeedtestResult speedtestResult = new()
		{
			DownloadedBytes = totalBytesRead,
			ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
			MaxBytesPerSecond = measurements.Count != 0 ? measurements.GroupBy(m => m.Key / 1000).Max(s => GetSpeedInBytes(s, 1000)) : 0
		};

		if (measurements.Count != 0)
		{
			Program.Logger.Information("Downloaded: {downloadedMBs:F2} MB, Time: {elapsedMilliseconds} ms, Speed: {maxBytesPerSecond:F1)}/s ({maxBitsPerSecond:F0}/s)", speedtestResult.DownloadedMBs, speedtestResult.ElapsedMilliseconds, FileSizeHelper.ToHumanReadable(speedtestResult.MaxBytesPerSecond), FileSizeHelper.ToHumanReadable(speedtestResult.MaxBytesPerSecond * 8, true));
		}
		else
		{
			Program.Logger.Warning("Speedtest failed, nothing downloaded.");
		}

		return speedtestResult;
	}

	private static void ClearCurrentLine()
	{
		try
		{
			if (!Console.IsOutputRedirected)
			{
				Console.Write(new string('+', Console.WindowWidth).Replace("+", "\b \b"));
			}
			else
			{
				Console.WriteLine();
			}
		}
		catch
		{
			// Happens when console is redirected, and just to be sure
			Console.WriteLine();
		}
	}

	private static Uri GetUrlDirectory(string url)
	{
		return new Uri(new Uri(url), ".");
	}

	public static DateTime UnixTimestampToDateTime(long unixTimeStamp)
	{
		return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixTimeStamp);
	}

	public static Stream GetEmbeddedResourceStream(Assembly assembly, string resourceFileName)
	{
		List<string> resourcePaths = assembly.GetManifestResourceNames().Where(x => x.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase)).ToList();

		return resourcePaths.Count == 1 ? assembly.GetManifestResourceStream(resourcePaths.Single()) : null;
	}

	public static bool GetUriCredentials(Uri uri, out string username, out string password)
	{
		username = null;
		password = null;

		if (uri.UserInfo?.Contains(':') != true)
		{
			return false;
		}

		string[] splitted = uri.UserInfo.Split(':');

		username = WebUtility.UrlDecode(splitted.First());
		password = WebUtility.UrlDecode(splitted.Last());

		return true;
	}

	public static AsyncRetryPolicy GetAsyncRetryPolicy(Action<Exception, TimeSpan, int, Context> onRetry, int maxRetries = 4)
	{
		return Policy
			.Handle<Exception>()
			.WaitAndRetryAsync(maxRetries,
				sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, retryAttempt))),
				onRetry: onRetry
			);
	}

	public static async Task<string> GetSourceMapUrlFromJavaScriptAsync(HttpClient httpClient, string url)
	{
		HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url);

		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			return null;
		}

		string javaScript = await httpResponseMessage.Content.ReadAsStringAsync();

		Regex regex = new(@"\/\/# sourceMappingURL=(?<SourceMapUrl>.*)");

		Match regexMatch = regex.Match(javaScript);

		return !regexMatch.Success ? null : regexMatch.Groups["SourceMapUrl"].Value;
	}

	public static async IAsyncEnumerable<string> GetSourcesFromSourceMapAsync(HttpClient httpClient, string sourceUrl)
	{
		await using Stream httpStream = await httpClient.GetStreamAsync(sourceUrl);
		using StreamReader streamReader = new(httpStream);
		await using JsonReader jsonReader = new JsonTextReader(streamReader);

		JObject jObject = await JObject.LoadAsync(jsonReader);

		if (!jObject.TryGetValue("sources", out JToken sources))
		{
			yield break;
		}

		foreach (JToken source in sources)
		{
			yield return source.Value<string>();
		}
	}

	public static void ProcessUrl(string baseUrl, IElement link, out string linkHref, out Uri uri, out string fullUrl)
	{
		linkHref = link.Attributes["href"]?.Value;
		uri = new Uri(new Uri(baseUrl), linkHref);
		fullUrl = uri.ToString();
	}

	/// <summary>
	/// Check and fix some common bad charsets
	/// </summary>
	/// <param name="httpResponseMessage">Fixed charset</param>
	public static void FixCharSet(HttpResponseMessage httpResponseMessage)
	{
		if (httpResponseMessage.Content.Headers.ContentType?.CharSet?.ToLowerInvariant() == "utf8" ||
			httpResponseMessage.Content.Headers.ContentType?.CharSet?.ToLowerInvariant() == "\"utf-8\"" ||
			httpResponseMessage.Content.Headers.ContentType?.CharSet == "GB1212")
		{
			httpResponseMessage.Content.Headers.ContentType.CharSet = "UTF-8";
		}

		if (httpResponseMessage.Content.Headers.ContentType?.CharSet == "WIN-1251")
		{
			httpResponseMessage.Content.Headers.ContentType.CharSet = "Windows-1251";
		}
	}

	public static async Task<string> GetHtml(HttpResponseMessage httpResponseMessage)
	{
		FixCharSet(httpResponseMessage);

		return await httpResponseMessage.Content.ReadAsStringAsync();
	}

	public static async Task<string> GetHtml(Stream stream)
	{
		using StreamReader streamReader = new(stream);

		return await streamReader.ReadToEndAsync();
	}
}
