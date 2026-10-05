using AngleSharp.Dom;
using FluentFTP;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using OpenDirectoryDownloader.Storage;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

	/// <summary>Writes the URL list one line at a time (no second full copy of every URL), '#' escaped and normalized to an absolute URI where possible.</summary>
	public static void WriteUrlList(string path, IEnumerable<string> urls)
	{
		using StreamWriter streamWriter = new(path);

		foreach (string url in urls)
		{
			string safeUrl = url.Contains('#') ? url.Replace("#", "%23") : url;

			streamWriter.WriteLine(Uri.TryCreate(safeUrl, UriKind.Absolute, out Uri uri) ? uri.AbsoluteUri : safeUrl);
		}
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
		using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write);

		JsonSerializer.Serialize(fileStream, session);
	}

	/// <summary>
	/// Properties of <see cref="Session"/> to serialize when streaming it out, in declaration order, computed
	/// once via reflection the same way the old JObject.FromObject(session) did implicitly. Excludes anything
	/// marked [JsonIgnore] so this stays in sync automatically as Session gains/loses properties.
	/// </summary>
	private static readonly PropertyInfo[] SessionJsonProperties = [.. typeof(Session)
		.GetProperties(BindingFlags.Public | BindingFlags.Instance)
		.Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null)];

	/// <summary>
	/// Same output as SaveSessionJson, except the Root directory tree is rebuilt from <paramref
	/// name="scanDatabase"/> instead of walked from the in-memory session.Root. Needed once eviction
	/// (issue #56 phase 4) may have dropped some directories' Files/Subdirectories from memory - the
	/// in-memory tree alone would silently produce incomplete JSON for anything under an evicted
	/// directory.
	///
	/// Every other Session field is written straight through via the normal reflection-based serializer.
	/// The Root tree, which can be arbitrarily large, is instead streamed directly into the output
	/// Utf8JsonWriter one directory/file at a time as it's read from the database - unlike the old
	/// JObject.FromObject + BuildDirectoryJTokenAsync approach, this never materializes the whole tree
	/// (or even one full directory level of it) as an in-memory JSON object graph before writing it out.
	/// </summary>
	public static async Task SaveSessionJsonAsync(Session session, string filePath, ScanDatabase scanDatabase)
	{
		await scanDatabase.FlushAsync();

		await using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write);
		await using Utf8JsonWriter writer = new(fileStream);

		writer.WriteStartObject();

		foreach (PropertyInfo property in SessionJsonProperties)
		{
			writer.WritePropertyName(property.Name);

			if (property.Name == nameof(Session.Root))
			{
				await WriteDirectoryAsync(writer, scanDatabase, session.Root.Url);
			}
			else
			{
				JsonSerializer.Serialize(writer, property.GetValue(session), property.PropertyType);
			}
		}

		writer.WriteEndObject();

		await writer.FlushAsync();
	}

	private static async Task WriteDirectoryAsync(Utf8JsonWriter writer, ScanDatabase scanDatabase, string url)
	{
		ScanDatabase.MirroredDirectory directory = await scanDatabase.GetDirectoryAsync(url);

		writer.WriteStartObject();

		if (directory is null)
		{
			// Never mirrored (e.g. --use-database was turned on after the scan started, or the directory
			// errored before ever being written) - fall back to an empty-but-valid node rather than null.
			writer.WriteString("Url", url);
			writer.WriteString("Name", string.Empty);
			writer.WriteNull("Description");
			writer.WriteBoolean("Finished", false);
			writer.WriteStartArray("Subdirectories");
			writer.WriteEndArray();
			writer.WriteStartArray("Files");
			writer.WriteEndArray();
			writer.WriteBoolean("Error", false);
			writer.WriteEndObject();

			return;
		}

		writer.WriteString("Url", directory.Url);
		writer.WriteString("Name", directory.Name);
		writer.WriteString("Description", directory.Description);
		writer.WriteBoolean("Finished", directory.Finished);

		writer.WriteStartArray("Subdirectories");

		List<ScanDatabase.MirroredDirectory> subdirectories = await scanDatabase.GetSubdirectoriesAsync(url);

		foreach (ScanDatabase.MirroredDirectory subdirectory in subdirectories)
		{
			await WriteDirectoryAsync(writer, scanDatabase, subdirectory.Url);
		}

		writer.WriteEndArray();

		writer.WriteStartArray("Files");

		List<ScanDatabase.MirroredFile> files = await scanDatabase.GetFilesAsync(url);

		foreach (ScanDatabase.MirroredFile file in files)
		{
			writer.WriteStartObject();
			writer.WriteString("Url", file.Url);
			writer.WriteString("FileName", file.FileName);

			if (file.FileSize.HasValue)
			{
				writer.WriteNumber("FileSize", file.FileSize.Value);
			}
			else
			{
				writer.WriteNull("FileSize");
			}

			writer.WriteString("Description", file.Description);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();

		writer.WriteBoolean("Error", directory.Error);

		writer.WriteEndObject();
	}

	public static string CleanUriToFilename(Uri uri)
	{
		return PathHelper.GetValidPath(Uri.UnescapeDataString(uri.ToString()));
	}

	public static Session LoadSessionJson(string fileName)
	{
		using FileStream fileStream = new(fileName, FileMode.Open, FileAccess.Read);

		return JsonSerializer.Deserialize<Session>(fileStream);
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

					if (perSecond.Count == 0)
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

		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(httpStream);

		if (!jsonDocument.RootElement.TryGetProperty("sources", out JsonElement sources))
		{
			yield break;
		}

		foreach (JsonElement source in sources.EnumerateArray())
		{
			yield return source.GetString();
		}
	}

	public static void ProcessUrl(string baseUrl, IElement link, out string linkHref, out Uri uri, out string fullUrl)
	{
		ProcessUrl(baseUrl, link.Attributes["href"]?.Value, out linkHref, out uri, out fullUrl);
	}

	// A listing resolves every line against the same base URL: parse it once per thread, not once per line
	[ThreadStatic]
	private static string _lastBaseUrl;

	[ThreadStatic]
	private static Uri _lastBaseUri;

	/// <summary>
	/// The file name of a listing entry, for the Uri ProcessUrl made and its fullUrl (always uri.ToString()).
	/// This was <c>Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath))</c>: a second parse of the
	/// same URL for every file. The path of the Uri we already have is the same, except when it holds percent
	/// escapes: ToString() unescapes some of them, so re-parsing can escape them differently (%78 and %x, say).
	/// Those keep the old way. Checked on millions of random hrefs without a single difference when there is no '%'.
	/// </summary>
	public static string GetFileNameFromUrl(Uri uri, string fullUrl)
	{
		string path = uri.AbsolutePath;

		if (path.Contains('%'))
		{
			path = new Uri(fullUrl).AbsolutePath;
		}

		return Path.GetFileName(WebUtility.UrlDecode(path));
	}

	public static void ProcessUrl(string baseUrl, string href, out string linkHref, out Uri uri, out string fullUrl)
	{
		if (!ReferenceEquals(_lastBaseUrl, baseUrl) && _lastBaseUrl != baseUrl)
		{
			_lastBaseUri = new Uri(baseUrl);
			_lastBaseUrl = baseUrl;
		}

		linkHref = href;
		uri = new Uri(_lastBaseUri, linkHref);
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
		using StreamReader streamReader = new(stream, Encoding.UTF8);

		return await streamReader.ReadToEndAsync();
	}
}
