using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace OpenDirectoryDownloader.Site.GDIndex.GoIndex;

public partial class GoIndexResponse
{
	[JsonProperty("error")]
	public Error Error { get; set; }

	[JsonProperty("files")]
	public List<File> Files { get; set; }
}

public partial class Error
{
	[JsonProperty("code")]
	public long Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
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

	[JsonProperty("size")]
	public long Size { get; set; }
}

public partial class GoIndexResponse
{
	public static GoIndexResponse FromJson(string json) => JsonConvert.DeserializeObject<GoIndexResponse>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GoIndexResponse self) => JsonConvert.SerializeObject(self, Converter.Settings);
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
