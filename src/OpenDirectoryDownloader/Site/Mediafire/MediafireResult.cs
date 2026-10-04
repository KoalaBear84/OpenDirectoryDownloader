using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.Mediafire;

public partial class MediafireResult
{
	[JsonPropertyName("response")]
	public Response Response { get; set; }
}

public partial class Response
{
	[JsonPropertyName("action")]
	public string Action { get; set; }

	[JsonPropertyName("asynchronous")]
	public string Asynchronous { get; set; }

	[JsonPropertyName("folder_content")]
	public FolderContent FolderContent { get; set; }

	[JsonPropertyName("result")]
	public string Result { get; set; }

	[JsonPropertyName("current_api_version")]
	public string CurrentApiVersion { get; set; }
}

public partial class FolderContent
{
	[JsonPropertyName("chunk_size")]
	public long ChunkSize { get; set; }

	[JsonPropertyName("content_type")]
	public string ContentType { get; set; }

	[JsonPropertyName("chunk_number")]
	public long ChunkNumber { get; set; }

	[JsonPropertyName("folderkey")]
	public string Folderkey { get; set; }

	[JsonPropertyName("folders")]
	public Folder[] Folders { get; set; }

	[JsonPropertyName("files")]
	public File[] Files { get; set; }

	[JsonPropertyName("more_chunks")]
	public string MoreChunks { get; set; }

	[JsonPropertyName("revision")]
	public long Revision { get; set; }
}

public partial class File
{
	[JsonPropertyName("quickkey")]
	public string Quickkey { get; set; }

	[JsonPropertyName("hash")]
	public string Hash { get; set; }

	[JsonPropertyName("filename")]
	public string Filename { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("privacy")]
	public string Privacy { get; set; }

	[JsonPropertyName("created")]
	public DateTimeOffset Created { get; set; }

	[JsonPropertyName("password_protected")]
	public string PasswordProtected { get; set; }

	[JsonPropertyName("mimetype")]
	public string Mimetype { get; set; }

	[JsonPropertyName("filetype")]
	public string Filetype { get; set; }

	[JsonPropertyName("view")]
	public long View { get; set; }

	[JsonPropertyName("edit")]
	public long Edit { get; set; }

	[JsonPropertyName("revision")]
	public long Revision { get; set; }

	[JsonPropertyName("flag")]
	public long Flag { get; set; }

	[JsonPropertyName("permissions")]
	public Permissions Permissions { get; set; }

	[JsonPropertyName("downloads")]
	public long Downloads { get; set; }

	[JsonPropertyName("views")]
	public long Views { get; set; }

	[JsonPropertyName("links")]
	public Links Links { get; set; }

	[JsonPropertyName("created_utc")]
	public DateTimeOffset CreatedUtc { get; set; }
}

public partial class Links
{
	[JsonPropertyName("normal_download")]
	public Uri NormalDownload { get; set; }
}

public partial class Permissions
{
	[JsonPropertyName("value")]
	public long Value { get; set; }

	[JsonPropertyName("explicit")]
	public long Explicit { get; set; }

	[JsonPropertyName("read")]
	public long Read { get; set; }

	[JsonPropertyName("write")]
	public long Write { get; set; }
}

public partial class Folder
{
	[JsonPropertyName("folderkey")]
	public string Folderkey { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("tags")]
	public string Tags { get; set; }

	[JsonPropertyName("privacy")]
	public string Privacy { get; set; }

	[JsonPropertyName("created")]
	public DateTimeOffset Created { get; set; }

	[JsonPropertyName("revision")]
	public long Revision { get; set; }

	[JsonPropertyName("flag")]
	public long Flag { get; set; }

	[JsonPropertyName("permissions")]
	public Permissions Permissions { get; set; }

	[JsonPropertyName("file_count")]
	public long FileCount { get; set; }

	[JsonPropertyName("folder_count")]
	public long FolderCount { get; set; }

	[JsonPropertyName("dropbox_enabled")]
	public string DropboxEnabled { get; set; }

	[JsonPropertyName("created_utc")]
	public DateTimeOffset CreatedUtc { get; set; }
}

public partial class MediafireResult
{
	public static MediafireResult FromJson(string json) => JsonSerializer.Deserialize<MediafireResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this MediafireResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
