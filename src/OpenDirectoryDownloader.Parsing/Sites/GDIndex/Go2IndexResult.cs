using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Parsing.Json;

namespace OpenDirectoryDownloader.Parsing.Sites.GDIndex.Go2Index;

public partial class Go2IndexResponse
{
	[JsonPropertyName("nextPageToken")]
	public string NextPageToken { get; set; }

	[JsonPropertyName("curPageIndex")]
	public int CurPageIndex { get; set; }

	[JsonPropertyName("data")]
	public Data Data { get; set; }

	[JsonPropertyName("error")]
	public Error Error { get; set; }
}

public partial class Data
{
	[JsonPropertyName("files")]
	public List<File> Files { get; set; }

	// Needed for alx-xlx/goindex
	[JsonPropertyName("error")]
	public Error Error { get; set; }
}

public partial class File
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("mimeType")]
	public string MimeType { get; set; }

	[JsonPropertyName("modifiedTime")]
	public DateTimeOffset ModifiedTime { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }
}

public partial class Error
{
	[JsonPropertyName("code")]
	public int Code { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }
}

public partial class Go2IndexResponse
{
	public static Go2IndexResponse FromJson(string json) => JsonSerializer.Deserialize(json, Converter.TypeInfo<Go2IndexResponse>());
}

public static class Serialize
{
	public static string ToJson(this Go2IndexResponse self) => JsonSerializer.Serialize(self, Converter.TypeInfo<Go2IndexResponse>());
}

internal static class Converter
{
	public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
	{
		TypeInfoResolver = Go2IndexResultJsonContext.Default,
		Converters =
		{
			JsonMetadataServices.DateOnlyConverter,
			new TimeOnlyConverter(),
			new IsoDateTimeOffsetConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
		},
	};

	/// <summary>Source generated, so the trimmed release keeps what is needed</summary>
	public static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Settings.GetTypeInfo(typeof(T));
}

[JsonSerializable(typeof(Go2IndexResponse))]
internal partial class Go2IndexResultJsonContext : JsonSerializerContext;
