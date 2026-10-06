using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Parsing.Listings;

public class DirectoryListingModel01
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("items")]
	public List<DirectoryListingModel01> Items { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }
}

[JsonSerializable(typeof(DirectoryListingModel01))]
internal sealed partial class ListingJsonContext : JsonSerializerContext;
