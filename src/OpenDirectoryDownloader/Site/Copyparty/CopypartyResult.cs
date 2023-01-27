using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace OpenDirectoryDownloader.Site.Copyparty;

public partial class CopypartyListing
{
	[JsonProperty("dirs")]
	public Dir[] Dirs { get; set; }

	[JsonProperty("files")]
	public Dir[] Files { get; set; }

	[JsonProperty("taglist")]
	public object[] Taglist { get; set; }
}

public partial class Dir
{
	[JsonProperty("dt")]
	public DateTimeOffset Dt { get; set; }

	[JsonProperty("ext")]
	public string Ext { get; set; }

	[JsonProperty("href")]
	public string Href { get; set; }

	[JsonProperty("lead")]
	public string Lead { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("sz")]
	public long Sz { get; set; }

	[JsonProperty("tags")]
	public Tags Tags { get; set; }

	[JsonProperty("ts")]
	public long Ts { get; set; }
}

public partial class Tags
{
}

public partial class CopypartyListing
{
	public static CopypartyListing FromJson(string json) => JsonConvert.DeserializeObject<CopypartyListing>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this CopypartyListing self) => JsonConvert.SerializeObject(self, Converter.Settings);
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
