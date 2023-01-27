using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace OpenDirectoryDownloader.Site.GDIndex.GdIndex;

public partial class GdIndexResponse
{
	[JsonProperty("files")]
	public List<File> Files { get; set; }
}

public partial class File
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("mimeType")]
	public string MimeType { get; set; }

	[JsonProperty("modifiedTime")]
	public DateTimeOffset ModifiedTime { get; set; }

	[JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
	public long Size { get; set; }
}

public partial class GdIndexResponse
{
	public static GdIndexResponse FromJson(string json) => JsonConvert.DeserializeObject<GdIndexResponse>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GdIndexResponse self) => JsonConvert.SerializeObject(self, Converter.Settings);
}

internal static class Converter
{
	public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
	{
		MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
		Converters =
		{
			new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
		},
	};
}
