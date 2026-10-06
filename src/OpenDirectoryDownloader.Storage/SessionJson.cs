using OpenDirectoryDownloader.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Storage;

/// <summary>
/// The saved session (-j / the J key), format version 2: a small header (the scan, its numbers, the URLs with
/// errors) and then the whole directory tree, streamed straight from the scan database. The tree comes last, so
/// a reader can stop after the header. No passwords, tokens or header values are written.
/// </summary>
public sealed class SessionHeader
{
	public const string FormatName = "OpenDirectoryDownloader session";

	public string Format { get; set; } = FormatName;

	public int Version { get; set; } = 2;

	public SessionScanInfo Scan { get; set; } = new();

	public SessionStats Stats { get; set; } = new();

	public List<string> UrlsWithErrors { get; set; } = [];

	public List<string> PossibleAlternativeUrls { get; set; } = [];
}

public sealed class SessionScanInfo
{
	public string RootUrl { get; set; }

	public DateTimeOffset Started { get; set; }

	public DateTimeOffset? Finished { get; set; }

	public string AppVersion { get; set; }

	public string Description { get; set; }

	/// <summary>The options the scan ran with, by their command line name; secrets left out.</summary>
	public Dictionary<string, string> Options { get; set; } = [];
}

public sealed class SessionStats
{
	public long TotalFiles { get; set; }

	public long TotalFileSize { get; set; }

	public long TotalDirectories { get; set; }

	public long HttpRequests { get; set; }

	public long HttpTraffic { get; set; }

	public long Errors { get; set; }

	public long Skipped { get; set; }

	public Dictionary<int, int> HttpStatusCodes { get; set; } = [];

	public SessionSpeedtest Speedtest { get; set; }

	public string UploadedUrlsUrl { get; set; }
}

public sealed class SessionSpeedtest
{
	public long DownloadedBytes { get; set; }

	public long ElapsedMilliseconds { get; set; }

	public long MaxBytesPerSecond { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionHeader))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;

public static class SessionJsonWriter
{
	/// <summary>Writes the session with the tree below <paramref name="rootUrl"/> read from <paramref name="scanDatabase"/>.</summary>
	public static async Task WriteAsync(string path, SessionHeader header, ScanDatabase scanDatabase, string rootUrl, CancellationToken cancellationToken = default)
	{
		await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 1 << 20, FileOptions.Asynchronous);
		await using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { MaxDepth = int.MaxValue, SkipValidation = true });

		writer.WriteStartObject();
		writer.WriteString("format", header.Format);
		writer.WriteNumber("version", header.Version);

		writer.WritePropertyName("scan");
		JsonSerializer.Serialize(writer, header.Scan, SessionJsonContext.Default.SessionScanInfo);
		writer.WritePropertyName("stats");
		JsonSerializer.Serialize(writer, header.Stats, SessionJsonContext.Default.SessionStats);
		writer.WritePropertyName("urlsWithErrors");
		JsonSerializer.Serialize(writer, header.UrlsWithErrors, SessionJsonContext.Default.ListString);
		writer.WritePropertyName("possibleAlternativeUrls");
		JsonSerializer.Serialize(writer, header.PossibleAlternativeUrls, SessionJsonContext.Default.ListString);

		writer.WritePropertyName("root");
		await scanDatabase.WalkTreeAsync(rootUrl, new TreeWriter(writer, stream), cancellationToken);

		writer.WriteEndObject();
		await writer.FlushAsync(cancellationToken);
	}

	/// <summary>Writes the tree as it is walked, flushing every so often so the writer's buffer stays small.</summary>
	private sealed class TreeWriter(Utf8JsonWriter writer, Stream stream) : ScanDatabase.ITreeVisitor
	{
		private const int FlushThreshold = 1 << 20;

		public async Task MissingRootAsync(string rootUrl)
		{
			writer.WriteStartObject();
			writer.WriteString("url", rootUrl);
			writer.WriteString("name", string.Empty);
			writer.WriteBoolean("finished", false);
			writer.WriteBoolean("error", false);
			writer.WriteStartArray("files");
			writer.WriteEndArray();
			writer.WriteStartArray("subdirectories");
			writer.WriteEndArray();
			writer.WriteEndObject();

			await Task.CompletedTask;
		}

		public Task EnterDirectoryAsync(ScanDatabase.MirroredDirectory directory)
		{
			writer.WriteStartObject();
			writer.WriteString("url", directory.Url);
			writer.WriteString("name", directory.Name);

			if (directory.Description is not null)
			{
				writer.WriteString("description", directory.Description);
			}

			writer.WriteBoolean("finished", directory.Finished);
			writer.WriteBoolean("error", directory.Error);
			writer.WriteStartArray("files");

			return Task.CompletedTask;
		}

		public async Task FileAsync(ScanDatabase.MirroredDirectory directory, ScanDatabase.MirroredFile file)
		{
			writer.WriteStartObject();
			writer.WriteString("url", file.Url);
			writer.WriteString("name", file.FileName);

			if (file.FileSize is long size)
			{
				writer.WriteNumber("size", size);
			}

			if (file.Description is not null)
			{
				writer.WriteString("description", file.Description);
			}

			writer.WriteEndObject();

			await FlushWhenFullAsync();
		}

		public Task BeginSubdirectoriesAsync(ScanDatabase.MirroredDirectory directory)
		{
			writer.WriteEndArray();
			writer.WriteStartArray("subdirectories");

			return Task.CompletedTask;
		}

		public async Task LeaveDirectoryAsync(ScanDatabase.MirroredDirectory directory)
		{
			writer.WriteEndArray();
			writer.WriteEndObject();

			await FlushWhenFullAsync();
		}

		private async Task FlushWhenFullAsync()
		{
			if (writer.BytesPending >= FlushThreshold)
			{
				await writer.FlushAsync();
				await stream.FlushAsync();
			}
		}
	}
}

/// <summary>The totals of a saved session, read without building its tree in memory.</summary>
public sealed record SessionSummary(SessionHeader Header, Dictionary<string, ExtensionStats> Extensions, long TotalFiles, long TotalFileSize, long TotalDirectories);

/// <summary>
/// Reads a session file token by token, so a session of any size or depth is read in a small, fixed amount of
/// memory: the header into a <see cref="SessionHeader"/>, the tree only counted (files, sizes, extensions).
/// </summary>
public static class SessionJsonReader
{
	public static SessionSummary ReadSummary(string path)
	{
		SessionHeader header = new() { Format = null, Version = 0 };
		Dictionary<string, ExtensionStats> extensions = [];
		long totalFiles = 0;
		long totalFileSize = 0;
		long totalDirectories = 0;

		// What we are in: one entry per open object or array, with the property that led to it
		List<(bool IsArray, string Property)> frames = [];
		string currentProperty = null;

		// The file object being read
		string fileName = null;
		long? fileSize = null;

		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);

		byte[] buffer = new byte[1 << 16];
		int dataLength = stream.Read(buffer, 0, buffer.Length);
		bool isFinalBlock = dataLength == 0;
		JsonReaderState state = new(new JsonReaderOptions { MaxDepth = int.MaxValue });

		// UTF-8 byte order mark
		int start = dataLength >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;

		while (true)
		{
			Utf8JsonReader reader = new(buffer.AsSpan(start, dataLength - start), isFinalBlock, state);

			while (reader.Read())
			{
				switch (reader.TokenType)
				{
					case JsonTokenType.StartObject:
					case JsonTokenType.StartArray:
						bool isArray = reader.TokenType == JsonTokenType.StartArray;

						if (!isArray && IsTreeArray(frames, "files"))
						{
							fileName = null;
							fileSize = null;
						}

						frames.Add((isArray, currentProperty ?? (frames.Count > 0 && frames[^1].IsArray ? frames[^1].Property : null)));
						currentProperty = null;
						break;
					case JsonTokenType.EndObject:
					case JsonTokenType.EndArray:
						frames.RemoveAt(frames.Count - 1);

						if (reader.TokenType == JsonTokenType.EndObject && IsTreeArray(frames, "files"))
						{
							totalFiles++;
							totalFileSize += fileSize ?? 0;

							string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

							if (!extensions.TryGetValue(extension, out ExtensionStats stats))
							{
								extensions[extension] = stats = new ExtensionStats();
							}

							stats.Count++;
							stats.FileSize += fileSize ?? 0;
						}

						currentProperty = null;
						break;
					case JsonTokenType.PropertyName:
						currentProperty = reader.GetString();
						break;
					default:
						ReadValue(ref reader, frames, currentProperty, header, ref fileName, ref fileSize, ref totalDirectories);
						currentProperty = null;
						break;
				}
			}

			if (isFinalBlock)
			{
				break;
			}

			state = reader.CurrentState;

			// Keep the part of the buffer the reader could not finish, read more after it
			int consumed = start + (int)reader.BytesConsumed;
			int remaining = dataLength - consumed;
			Buffer.BlockCopy(buffer, consumed, buffer, 0, remaining);
			start = 0;

			if (remaining == buffer.Length)
			{
				Array.Resize(ref buffer, buffer.Length * 2);
			}

			int read = stream.Read(buffer, remaining, buffer.Length - remaining);
			dataLength = remaining + read;
			isFinalBlock = read == 0;
		}

		if (header.Format != SessionHeader.FormatName)
		{
			throw new InvalidDataException($"'{path}' is not a session saved by this version of OpenDirectoryDownloader.");
		}

		return new SessionSummary(header, extensions, totalFiles, totalFileSize, totalDirectories);
	}

	/// <summary>Whether the innermost open container is a tree array named <paramref name="property"/> ("files" or "subdirectories")</summary>
	private static bool IsTreeArray(List<(bool IsArray, string Property)> frames, string property)
	{
		return frames.Count >= 2 && frames[^1].IsArray && frames[^1].Property == property && InTree(frames);
	}

	private static bool InTree(List<(bool IsArray, string Property)> frames) => frames.Count >= 2 && frames[1].Property == "root";

	private static void ReadValue(ref Utf8JsonReader reader, List<(bool IsArray, string Property)> frames, string property, SessionHeader header, ref string fileName, ref long? fileSize, ref long totalDirectories)
	{
		if (InTree(frames))
		{
			// A file object: in a "files" array
			if (frames.Count >= 2 && !frames[^1].IsArray && frames[^2].IsArray && frames[^2].Property == "files")
			{
				switch (property)
				{
					case "name":
						fileName = reader.GetString();
						break;
					case "size":
						fileSize = reader.TokenType == JsonTokenType.Number ? reader.GetInt64() : null;
						break;
				}
			}
			else if (property == "finished" && reader.TokenType == JsonTokenType.True && frames.Count > 2)
			{
				// Finished subdirectories, the root is not counted (the statistics add it)
				totalDirectories++;
			}

			return;
		}

		string section = frames.Count >= 2 ? frames[1].Property : null;

		switch (frames.Count)
		{
			case 1:
				switch (property)
				{
					case "format":
						header.Format = reader.GetString();
						break;
					case "version":
						header.Version = reader.GetInt32();
						break;
				}

				break;
			case 2 when section == "scan":
				switch (property)
				{
					case "rootUrl":
						header.Scan.RootUrl = reader.GetString();
						break;
					case "started":
						header.Scan.Started = reader.GetDateTimeOffset();
						break;
					case "finished":
						header.Scan.Finished = reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTimeOffset();
						break;
					case "appVersion":
						header.Scan.AppVersion = reader.GetString();
						break;
					case "description":
						header.Scan.Description = reader.GetString();
						break;
				}

				break;
			case 3 when section == "scan" && frames[2].Property == "options":
				header.Scan.Options[property] = reader.TokenType == JsonTokenType.String ? reader.GetString() : Encoding.UTF8.GetString(reader.ValueSpan);
				break;
			case 2 when section == "stats":
				switch (property)
				{
					case "totalFiles":
						header.Stats.TotalFiles = reader.GetInt64();
						break;
					case "totalFileSize":
						header.Stats.TotalFileSize = reader.GetInt64();
						break;
					case "totalDirectories":
						header.Stats.TotalDirectories = reader.GetInt64();
						break;
					case "httpRequests":
						header.Stats.HttpRequests = reader.GetInt64();
						break;
					case "httpTraffic":
						header.Stats.HttpTraffic = reader.GetInt64();
						break;
					case "errors":
						header.Stats.Errors = reader.GetInt64();
						break;
					case "skipped":
						header.Stats.Skipped = reader.GetInt64();
						break;
					case "uploadedUrlsUrl":
						header.Stats.UploadedUrlsUrl = reader.GetString();
						break;
				}

				break;
			case 3 when section == "stats" && frames[2].Property == "httpStatusCodes":
				header.Stats.HttpStatusCodes[int.Parse(property)] = reader.GetInt32();
				break;
			case 3 when section == "stats" && frames[2].Property == "speedtest":
				header.Stats.Speedtest ??= new SessionSpeedtest();

				switch (property)
				{
					case "downloadedBytes":
						header.Stats.Speedtest.DownloadedBytes = reader.GetInt64();
						break;
					case "elapsedMilliseconds":
						header.Stats.Speedtest.ElapsedMilliseconds = reader.GetInt64();
						break;
					case "maxBytesPerSecond":
						header.Stats.Speedtest.MaxBytesPerSecond = reader.GetInt64();
						break;
				}

				break;
			case 2 when section == "urlsWithErrors":
				header.UrlsWithErrors.Add(reader.GetString());
				break;
			case 2 when section == "possibleAlternativeUrls":
				header.PossibleAlternativeUrls.Add(reader.GetString());
				break;
		}
	}
}
