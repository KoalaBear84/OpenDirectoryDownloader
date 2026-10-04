using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.BlitzfilesTech;

public partial class BlitzfilesTechResponse
{
	[JsonPropertyName("link")]
	public Link Link { get; set; }

	[JsonPropertyName("folderChildren")]
	public FolderChildren FolderChildren { get; set; }

	[JsonPropertyName("status")]
	public string Status { get; set; }

	[JsonPropertyName("seo")]
	public object Seo { get; set; }
}

public partial class FolderChildren
{
	[JsonPropertyName("current_page")]
	public long CurrentPage { get; set; }

	[JsonPropertyName("data")]
	public List<Entry> Data { get; set; }

	[JsonPropertyName("from")]
	public long From { get; set; }

	[JsonPropertyName("last_page")]
	public long LastPage { get; set; }

	[JsonPropertyName("next_page_url")]
	public string NextPageUrl { get; set; }

	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("per_page")]
	public long PerPage { get; set; }

	[JsonPropertyName("prev_page_url")]
	public string PrevPageUrl { get; set; }

	[JsonPropertyName("to")]
	public long To { get; set; }

	[JsonPropertyName("total")]
	public long Total { get; set; }
}

public partial class Entry
{
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public object Description { get; set; }

	[JsonPropertyName("file_name")]
	public string FileName { get; set; }

	[JsonPropertyName("mime")]
	public string Mime { get; set; }

	[JsonPropertyName("file_size")]
	public long FileSize { get; set; }

	[JsonPropertyName("user_id")]
	public object UserId { get; set; }

	[JsonPropertyName("parent_id")]
	public long? ParentId { get; set; }

	[JsonPropertyName("password")]
	public object Password { get; set; }

	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	[JsonPropertyName("deleted_at")]
	public object DeletedAt { get; set; }

	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("disk_prefix")]
	public string DiskPrefix { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("extension")]
	public string Extension { get; set; }

	[JsonPropertyName("public")]
	public bool Public { get; set; }

	[JsonPropertyName("thumbnail")]
	public bool Thumbnail { get; set; }

	[JsonPropertyName("hash")]
	public string Hash { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }

	[JsonPropertyName("users")]
	public List<User> Users { get; set; }
}

public partial class User
{
	[JsonPropertyName("email")]
	public string Email { get; set; }

	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("avatar")]
	public Uri Avatar { get; set; }

	[JsonPropertyName("owns_entry")]
	public long OwnsEntry { get; set; }

	[JsonPropertyName("entry_permissions")]
	public object EntryPermissions { get; set; }

	[JsonPropertyName("display_name")]
	public string DisplayName { get; set; }
}

public partial class Link
{
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("hash")]
	public string Hash { get; set; }

	[JsonPropertyName("user_id")]
	public long UserId { get; set; }

	[JsonPropertyName("entry_id")]
	public long EntryId { get; set; }

	[JsonPropertyName("allow_edit")]
	public bool AllowEdit { get; set; }

	[JsonPropertyName("allow_download")]
	public bool AllowDownload { get; set; }

	[JsonPropertyName("password")]
	public object Password { get; set; }

	[JsonPropertyName("expires_at")]
	public object ExpiresAt { get; set; }

	[JsonPropertyName("created_at")]
	public DateTimeOffset CreatedAt { get; set; }

	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }

	[JsonPropertyName("entry")]
	public Entry Entry { get; set; }
}

public partial class BlitzfilesTechResponse
{
	public static BlitzfilesTechResponse FromJson(string json) => JsonSerializer.Deserialize<BlitzfilesTechResponse>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this BlitzfilesTechResponse self) => JsonSerializer.Serialize(self, Converter.Settings);
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
