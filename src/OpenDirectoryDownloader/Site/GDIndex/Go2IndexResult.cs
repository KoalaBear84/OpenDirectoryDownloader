using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.GDIndex.Go2Index;

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
	public static Go2IndexResponse FromJson(string json) => JsonSerializer.Deserialize<Go2IndexResponse>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this Go2IndexResponse self) => JsonSerializer.Serialize(self, Converter.Settings);
}

internal static class Converter
{
	public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
	{
		Converters =
		{
			JsonMetadataServices.DateOnlyConverter,
			new TimeOnlyConverter(),
			new IsoDateTimeOffsetConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
		},
	};
}
