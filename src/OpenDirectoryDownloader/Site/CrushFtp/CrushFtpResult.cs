using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.CrushFtp;

public partial class CrushFtpResult
{
	[JsonPropertyName("privs")]
	public string Privs { get; set; }

	[JsonPropertyName("comment")]
	public string Comment { get; set; }

	[JsonPropertyName("path")]
	public string Path { get; set; }

	[JsonPropertyName("defaultStrings")]
	public string DefaultStrings { get; set; }

	[JsonPropertyName("site")]
	public string Site { get; set; }

	[JsonPropertyName("quota")]
	public string Quota { get; set; }

	[JsonPropertyName("quota_bytes")]
	public string QuotaBytes { get; set; }

	[JsonPropertyName("bytes_sent")]
	public long BytesSent { get; set; }

	[JsonPropertyName("bytes_received")]
	public long BytesReceived { get; set; }

	[JsonPropertyName("max_upload_amount_day")]
	public long MaxUploadAmountDay { get; set; }

	[JsonPropertyName("max_upload_amount_month")]
	public long MaxUploadAmountMonth { get; set; }

	[JsonPropertyName("max_upload_amount")]
	public long MaxUploadAmount { get; set; }

	[JsonPropertyName("max_upload_amount_available")]
	public long MaxUploadAmountAvailable { get; set; }

	[JsonPropertyName("max_upload_amount_day_available")]
	public string MaxUploadAmountDayAvailable { get; set; }

	[JsonPropertyName("max_upload_amount_month_available")]
	public string MaxUploadAmountMonthAvailable { get; set; }

	[JsonPropertyName("max_download_amount")]
	public long MaxDownloadAmount { get; set; }

	[JsonPropertyName("max_download_amount_day")]
	public long MaxDownloadAmountDay { get; set; }

	[JsonPropertyName("max_download_amount_month")]
	public long MaxDownloadAmountMonth { get; set; }

	[JsonPropertyName("max_download_amount_available")]
	public long MaxDownloadAmountAvailable { get; set; }

	[JsonPropertyName("max_download_amount_day_available")]
	public string MaxDownloadAmountDayAvailable { get; set; }

	[JsonPropertyName("max_download_amount_month_available")]
	public string MaxDownloadAmountMonthAvailable { get; set; }

	[JsonPropertyName("listing")]
	public Listing[] Listing { get; set; }
}

public partial class Listing
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("dir")]
	public string Dir { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }

	[JsonPropertyName("root_dir")]
	public string RootDir { get; set; }

	[JsonPropertyName("href_path")]
	public string HrefPath { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("modified")]
	public string Modified { get; set; }

	[JsonPropertyName("created")]
	public string Created { get; set; }

	[JsonPropertyName("owner")]
	public string Owner { get; set; }

	[JsonPropertyName("group")]
	public string Group { get; set; }

	[JsonPropertyName("permissionsNum")]
	public string PermissionsNum { get; set; }

	[JsonPropertyName("keywords")]
	public string Keywords { get; set; }

	[JsonPropertyName("permissions")]
	public string Permissions { get; set; }

	[JsonPropertyName("num_items")]
	public long NumItems { get; set; }

	[JsonPropertyName("preview")]
	public long Preview { get; set; }

	[JsonPropertyName("dateFormatted")]
	public string DateFormatted { get; set; }

	[JsonPropertyName("createdDateFormatted")]
	public string CreatedDateFormatted { get; set; }

	[JsonPropertyName("sizeFormatted")]
	public string SizeFormatted { get; set; }
}

public partial class CrushFtpResult
{
	public static CrushFtpResult FromJson(string json) => JsonSerializer.Deserialize<CrushFtpResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this CrushFtpResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
