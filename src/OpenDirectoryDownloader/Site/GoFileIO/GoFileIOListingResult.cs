using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.GoFileIO;

public partial class GoFileIOListingResult
{
	[JsonPropertyName("status")]
	public string Status { get; set; }

	[JsonPropertyName("data")]
	public Data Data { get; set; }
}

public partial class Data
{
	// Only for /createAccount
	[JsonPropertyName("token")]
	public string Token { get; set; }

	[JsonPropertyName("isOwner")]
	public bool IsOwner { get; set; }

	[JsonPropertyName("id")]
	public Guid Id { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("parentFolder")]
	public Guid ParentFolder { get; set; }

	[JsonPropertyName("code")]
	public string Code { get; set; }

	[JsonPropertyName("createTime")]
	public long CreateTime { get; set; }

	[JsonPropertyName("public")]
	public bool Public { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("childs")]
	public Guid[] Childs { get; set; }

	[JsonPropertyName("totalDownloadCount")]
	public long TotalDownloadCount { get; set; }

	[JsonPropertyName("totalSize")]
	public long TotalSize { get; set; }

	[JsonPropertyName("contents")]
	public Dictionary<string, Content> Contents { get; set; }
}

public partial class Content
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("parentFolder")]
	public Guid ParentFolder { get; set; }

	[JsonPropertyName("createTime")]
	public long CreateTime { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("downloadCount")]
	public long DownloadCount { get; set; }

	[JsonPropertyName("md5")]
	public string Md5 { get; set; }

	[JsonPropertyName("mimetype")]
	public string Mimetype { get; set; }

	[JsonPropertyName("serverChoosen")]
	public string ServerChoosen { get; set; }

	[JsonPropertyName("directLink")]
	public Uri DirectLink { get; set; }

	[JsonPropertyName("link")]
	public Uri Link { get; set; }
}

public partial class GoFileIOListingResult
{
	public static GoFileIOListingResult FromJson(string json) => JsonSerializer.Deserialize<GoFileIOListingResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GoFileIOListingResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
