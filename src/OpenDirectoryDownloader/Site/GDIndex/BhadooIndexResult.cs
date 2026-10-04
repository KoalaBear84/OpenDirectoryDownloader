using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.GDIndex.Bhadoo;

public partial class BhadooIndexResponse
{
	[JsonPropertyName("nextPageToken")]
	public string NextPageToken { get; set; }

	[JsonPropertyName("curPageIndex")]
	public long CurPageIndex { get; set; }

	[JsonPropertyName("data")]
	public Data Data { get; set; }

	[JsonPropertyName("error")]
	public Error Error { get; set; }
}

public partial class Error
{
	[JsonPropertyName("code")]
	public int Code { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }
}

public partial class Data
{
	[JsonPropertyName("nextPageToken")]
	public string NextPageToken { get; set; }

	[JsonPropertyName("files")]
	public List<File> Files { get; set; }

	[JsonPropertyName("error")]
	public DataError Error { get; set; }
}

public partial class DataError
{
	[JsonPropertyName("errors")]
	public List<ErrorElement> Errors { get; set; }

	[JsonPropertyName("code")]
	public long Code { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }
}

public partial class ErrorElement
{
	[JsonPropertyName("domain")]
	public string Domain { get; set; }

	[JsonPropertyName("reason")]
	public string Reason { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }
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

public partial class BhadooIndexResponse
{
	public static BhadooIndexResponse FromJson(string json) => JsonSerializer.Deserialize<BhadooIndexResponse>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this BhadooIndexResponse self) => JsonSerializer.Serialize(self, Converter.Settings);
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
