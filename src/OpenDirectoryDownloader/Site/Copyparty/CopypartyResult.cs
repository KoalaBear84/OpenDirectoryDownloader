using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.Copyparty;

public partial class CopypartyListing
{
	[JsonPropertyName("dirs")]
	public Dir[] Dirs { get; set; }

	[JsonPropertyName("files")]
	public Dir[] Files { get; set; }

	[JsonPropertyName("taglist")]
	public object[] Taglist { get; set; }
}

public partial class Dir
{
	[JsonPropertyName("dt")]
	public DateTimeOffset Dt { get; set; }

	[JsonPropertyName("ext")]
	public string Ext { get; set; }

	[JsonPropertyName("href")]
	public string Href { get; set; }

	[JsonPropertyName("lead")]
	public string Lead { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("sz")]
	public long Sz { get; set; }

	[JsonPropertyName("tags")]
	public Tags Tags { get; set; }

	[JsonPropertyName("ts")]
	public long Ts { get; set; }
}

public partial class Tags
{
}

public partial class CopypartyListing
{
	public static CopypartyListing FromJson(string json) => JsonSerializer.Deserialize<CopypartyListing>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this CopypartyListing self) => JsonSerializer.Serialize(self, Converter.Settings);
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
