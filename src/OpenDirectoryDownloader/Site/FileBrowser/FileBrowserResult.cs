using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace OpenDirectoryDownloader.Site.FileBrowser;

public partial class FileBrowserResult
{
	[JsonProperty("items")]
	public List<Item> Items { get; set; }

	[JsonProperty("numDirs")]
	public long NumDirs { get; set; }

	[JsonProperty("numFiles")]
	public long NumFiles { get; set; }

	[JsonProperty("sorting")]
	public Sorting Sorting { get; set; }

	[JsonProperty("path")]
	public string Path { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("extension")]
	public string Extension { get; set; }

	[JsonProperty("modified")]
	public DateTimeOffset Modified { get; set; }

	[JsonProperty("mode")]
	public long Mode { get; set; }

	[JsonProperty("isDir")]
	public bool IsDir { get; set; }

	[JsonProperty("isSymlink")]
	public bool IsSymlink { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

public partial class Item
{
	[JsonProperty("path")]
	public string Path { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("extension")]
	public string Extension { get; set; }

	[JsonProperty("modified")]
	public DateTimeOffset Modified { get; set; }

	[JsonProperty("mode")]
	public long Mode { get; set; }

	[JsonProperty("isDir")]
	public bool IsDir { get; set; }

	[JsonProperty("isSymlink")]
	public bool IsSymlink { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

public partial class Sorting
{
	[JsonProperty("by")]
	public string By { get; set; }

	[JsonProperty("asc")]
	public bool Asc { get; set; }
}

public partial class FileBrowserResult
{
	public static FileBrowserResult FromJson(string json) => JsonConvert.DeserializeObject<FileBrowserResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this FileBrowserResult self) => JsonConvert.SerializeObject(self, Converter.Settings);
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