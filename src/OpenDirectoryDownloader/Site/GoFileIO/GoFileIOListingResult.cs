using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace OpenDirectoryDownloader.Site.GoFileIO;

public partial class GoFileIOListingResult
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public Data Data { get; set; }
}

public partial class Data
{
	// Only for /createAccount
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("isOwner")]
	public bool IsOwner { get; set; }

	[JsonProperty("id")]
	public Guid Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("parentFolder")]
	public Guid ParentFolder { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("public")]
	public bool Public { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("childs")]
	public Guid[] Childs { get; set; }

	[JsonProperty("totalDownloadCount")]
	public long TotalDownloadCount { get; set; }

	[JsonProperty("totalSize")]
	public long TotalSize { get; set; }

	[JsonProperty("contents")]
	public Dictionary<string, Content> Contents { get; set; }
}

public partial class Content
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("parentFolder")]
	public Guid ParentFolder { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("downloadCount")]
	public long DownloadCount { get; set; }

	[JsonProperty("md5")]
	public string Md5 { get; set; }

	[JsonProperty("mimetype")]
	public string Mimetype { get; set; }

	[JsonProperty("serverChoosen")]
	public string ServerChoosen { get; set; }

	[JsonProperty("directLink")]
	public Uri DirectLink { get; set; }

	[JsonProperty("link")]
	public Uri Link { get; set; }
}

public partial class GoFileIOListingResult
{
	public static GoFileIOListingResult FromJson(string json) => JsonConvert.DeserializeObject<GoFileIOListingResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GoFileIOListingResult self) => JsonConvert.SerializeObject(self, Converter.Settings);
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
