using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.Pixeldrain.ListResult;

public partial class PixeldrainListResult
{
	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("api_response")]
	public ApiResponse ApiResponse { get; set; }

	[JsonPropertyName("captcha_key")]
	public string CaptchaKey { get; set; }

	[JsonPropertyName("view_token")]
	public string ViewToken { get; set; }

	[JsonPropertyName("embedded")]
	public bool Embedded { get; set; }

	[JsonPropertyName("user_ads_enabled")]
	public bool UserAdsEnabled { get; set; }
}

public partial class ApiResponse
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; }

	[JsonPropertyName("date_created")]
	public DateTimeOffset DateCreated { get; set; }

	[JsonPropertyName("file_count")]
	public long FileCount { get; set; }

	[JsonPropertyName("files")]
	public File[] Files { get; set; }

	[JsonPropertyName("can_edit")]
	public bool CanEdit { get; set; }
}

public partial class File
{
	[JsonPropertyName("detail_href")]
	public string DetailHref { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }

	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("views")]
	public long Views { get; set; }

	[JsonPropertyName("bandwidth_used")]
	public long BandwidthUsed { get; set; }

	[JsonPropertyName("bandwidth_used_paid")]
	public long BandwidthUsedPaid { get; set; }

	[JsonPropertyName("downloads")]
	public long Downloads { get; set; }

	[JsonPropertyName("date_upload")]
	public DateTimeOffset DateUpload { get; set; }

	[JsonPropertyName("date_last_view")]
	public DateTimeOffset DateLastView { get; set; }

	[JsonPropertyName("mime_type")]
	public string MimeType { get; set; }

	[JsonPropertyName("thumbnail_href")]
	public string ThumbnailHref { get; set; }

	[JsonPropertyName("hash_sha256")]
	public string HashSha256 { get; set; }

	[JsonPropertyName("availability")]
	public string Availability { get; set; }

	[JsonPropertyName("availability_message")]
	public string AvailabilityMessage { get; set; }

	[JsonPropertyName("abuse_type")]
	public string AbuseType { get; set; }

	[JsonPropertyName("abuse_reporter_name")]
	public string AbuseReporterName { get; set; }

	[JsonPropertyName("can_edit")]
	public bool CanEdit { get; set; }

	[JsonPropertyName("show_ads")]
	public bool ShowAds { get; set; }

	[JsonPropertyName("allow_video_player")]
	public bool AllowVideoPlayer { get; set; }

	[JsonPropertyName("download_speed_limit")]
	public long DownloadSpeedLimit { get; set; }
}

public partial class PixeldrainListResult
{
	public static PixeldrainListResult FromJson(string json) => JsonSerializer.Deserialize<PixeldrainListResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this PixeldrainListResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
