using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.FileBrowser;

public partial class FileBrowserResult
{
	[JsonPropertyName("items")]
	public List<Item> Items { get; set; }

	[JsonPropertyName("numDirs")]
	public long NumDirs { get; set; }

	[JsonPropertyName("numFiles")]
	public long NumFiles { get; set; }

	[JsonPropertyName("sorting")]
	public Sorting Sorting { get; set; }

	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("extension")]
	public string Extension { get; set; }

	[JsonPropertyName("modified")]
	public DateTimeOffset Modified { get; set; }

	[JsonPropertyName("mode")]
	public long Mode { get; set; }

	[JsonPropertyName("isDir")]
	public bool IsDir { get; set; }

	[JsonPropertyName("isSymlink")]
	public bool IsSymlink { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }
}

public partial class Item
{
	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("extension")]
	public string Extension { get; set; }

	[JsonPropertyName("modified")]
	public DateTimeOffset Modified { get; set; }

	[JsonPropertyName("mode")]
	public long Mode { get; set; }

	[JsonPropertyName("isDir")]
	public bool IsDir { get; set; }

	[JsonPropertyName("isSymlink")]
	public bool IsSymlink { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }
}

public partial class Sorting
{
	[JsonPropertyName("by")]
	public string By { get; set; }

	[JsonPropertyName("asc")]
	public bool Asc { get; set; }
}

public partial class FileBrowserResult
{
	public static FileBrowserResult FromJson(string json) => JsonSerializer.Deserialize<FileBrowserResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this FileBrowserResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
