using Newtonsoft.Json;
using System.Collections.Generic;

namespace OpenDirectoryDownloader.Models;

public class DirectoryListingModel01
{
	[JsonProperty(PropertyName = "name")]
	public string Name { get; set; }

	[JsonProperty(PropertyName = "type")]
	public string Type { get; set; }

	[JsonProperty(PropertyName = "path")]
	public string Path { get; set; }

	[JsonProperty(PropertyName = "items")]
	public List<DirectoryListingModel01> Items { get; set; }

	[JsonProperty(PropertyName = "size")]
	public long Size { get; set; }
}
