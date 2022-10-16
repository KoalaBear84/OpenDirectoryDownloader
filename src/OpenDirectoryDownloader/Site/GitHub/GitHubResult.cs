using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace OpenDirectoryDownloader.Site.GitHub;

/// <summary>
/// https://docs.github.com/en/rest/git/trees
/// </summary>
public partial class GitHubResult
{
	[JsonProperty("sha")]
	public string Sha { get; set; }

	[JsonProperty("url")]
	public Uri Url { get; set; }

	[JsonProperty("tree")]
	public Tree[] Tree { get; set; }

	[JsonProperty("truncated")]
	public bool Truncated { get; set; }
}

public partial class Tree
{
	[JsonProperty("path")]
	public string Path { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("sha")]
	public string Sha { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }

	[JsonProperty("size", NullValueHandling = NullValueHandling.Ignore)]
	public long Size { get; set; }
}

public partial class GitHubResult
{
	public static GitHubResult FromJson(string json) => JsonConvert.DeserializeObject<GitHubResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GitHubResult self) => JsonConvert.SerializeObject(self, Converter.Settings);
}

internal static class Converter
{
	public static readonly JsonSerializerSettings Settings = new()
	{
		MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
		Converters =
		{
			new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
		},
	};
}
