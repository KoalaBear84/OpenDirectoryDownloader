using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.GitHub;

/// <summary>
/// https://docs.github.com/en/rest/git/trees
/// </summary>
public partial class GitHubResult
{
	[JsonPropertyName("sha")]
	public string Sha { get; set; }

	[JsonPropertyName("url")]
	public Uri Url { get; set; }

	[JsonPropertyName("tree")]
	public Tree[] Tree { get; set; }

	[JsonPropertyName("truncated")]
	public bool Truncated { get; set; }
}

public partial class Tree
{
	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("mode")]
	public string Mode { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("sha")]
	public string Sha { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }
}

public partial class GitHubResult
{
	public static GitHubResult FromJson(string json) => JsonSerializer.Deserialize<GitHubResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this GitHubResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
