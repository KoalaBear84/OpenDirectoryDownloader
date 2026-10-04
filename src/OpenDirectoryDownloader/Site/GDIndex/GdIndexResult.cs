using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.GDIndex.GdIndex;

public partial class GdIndexResponse
{
	[JsonPropertyName("files")]
	public List<File> Files { get; set; }
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

public partial class GdIndexResponse
{
	public static GdIndexResponse FromJson(string json) => JsonSerializer.Deserialize<GdIndexResponse>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GdIndexResponse self) => JsonSerializer.Serialize(self, Converter.Settings);
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
