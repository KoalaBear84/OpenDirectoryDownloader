using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenDirectoryDownloader.Converters;

namespace OpenDirectoryDownloader.Site.Dropbox;

public partial class DropboxResult
{
	[JsonPropertyName("entries")]
	public Entry[] Entries { get; set; }

	[JsonPropertyName("share_tokens")]
	public ShareToken[] ShareTokens { get; set; }

	[JsonPropertyName("shared_link_infos")]
	public SharedLinkInfo[] SharedLinkInfos { get; set; }

	[JsonPropertyName("share_permissions")]
	public SharePermission[] SharePermissions { get; set; }

	[JsonPropertyName("takedown_request_type")]
	public object TakedownRequestType { get; set; }

	[JsonPropertyName("total_num_entries")]
	public long TotalNumEntries { get; set; }

	[JsonPropertyName("has_more_entries")]
	public bool HasMoreEntries { get; set; }

	[JsonPropertyName("next_request_voucher")]
	public string NextRequestVoucher { get; set; }

	[JsonPropertyName("folder")]
	public Folder Folder { get; set; }

	[JsonPropertyName("folder_share_permission")]
	public SharePermission FolderSharePermission { get; set; }

	[JsonPropertyName("folder_share_token")]
	public ShareToken FolderShareToken { get; set; }

	[JsonPropertyName("folder_shared_link_info")]
	public SharedLinkInfo FolderSharedLinkInfo { get; set; }
}

public partial class Entry
{
	[JsonPropertyName("bytes")]
	public long Bytes { get; set; }

	[JsonPropertyName("file_id")]
	public string FileId { get; set; }

	[JsonPropertyName("filename")]
	public string Filename { get; set; }

	[JsonPropertyName("href")]
	public Uri Href { get; set; }

	[JsonPropertyName("icon")]
	public string Icon { get; set; }

	[JsonPropertyName("is_dir")]
	public bool IsDir { get; set; }

	[JsonPropertyName("ns_id")]
	public long NsId { get; set; }

	[JsonPropertyName("open_in_app_data")]
	public object OpenInAppData { get; set; }

	[JsonPropertyName("preview")]
	public Preview Preview { get; set; }

	[JsonPropertyName("preview_type")]
	public string PreviewType { get; set; }

	[JsonPropertyName("revision_id")]
	public string RevisionId { get; set; }

	[JsonPropertyName("sjid")]
	public long Sjid { get; set; }

	[JsonPropertyName("sort_key")]
	public string[] SortKey { get; set; }

	[JsonPropertyName("thumbnail_url_tmpl")]
	public Uri ThumbnailUrlTmpl { get; set; }

	[JsonPropertyName("ts")]
	public long Ts { get; set; }

	[JsonPropertyName("is_symlink")]
	public bool IsSymlink { get; set; }
}

public partial class Preview
{
	[JsonPropertyName("content")]
	public Content Content { get; set; }

	[JsonPropertyName("preview_url")]
	public Uri PreviewUrl { get; set; }
}

public partial class Content
{
	[JsonPropertyName(".tag")]
	public string Tag { get; set; }

	[JsonPropertyName("text_url_tmpl")]
	public Uri TextUrlTmpl { get; set; }

	[JsonPropertyName("image_url_tmpl")]
	public Uri ImageUrlTmpl { get; set; }

	[JsonPropertyName("refresh_url")]
	public Uri RefreshUrl { get; set; }

	[JsonPropertyName("placeholder_image_url")]
	public Uri PlaceholderImageUrl { get; set; }

	[JsonPropertyName("autoprint_url")]
	public Uri AutoprintUrl { get; set; }

	[JsonPropertyName("supported_widths")]
	public long[] SupportedWidths { get; set; }
}

public partial class Folder
{
	[JsonPropertyName("_mount_access_perms")]
	public string[] MountAccessPerms { get; set; }

	[JsonPropertyName("filename")]
	public string Filename { get; set; }

	[JsonPropertyName("href")]
	public Uri Href { get; set; }

	[JsonPropertyName("is_dir")]
	public bool IsDir { get; set; }

	[JsonPropertyName("open_in_app_data")]
	public object OpenInAppData { get; set; }

	[JsonPropertyName("shared_folder_id")]
	public object SharedFolderId { get; set; }

	[JsonPropertyName("ns_id")]
	public long NsId { get; set; }

	[JsonPropertyName("sort_key")]
	public string[] SortKey { get; set; }

	[JsonPropertyName("folder_id")]
	public string FolderId { get; set; }
}

public partial class SharePermission
{
	[JsonPropertyName("canCopyToDropboxRoles")]
	public string[] CanCopyToDropboxRoles { get; set; }

	[JsonPropertyName("canSyncToDropboxRoles")]
	public object[] CanSyncToDropboxRoles { get; set; }

	[JsonPropertyName("canDownloadRoles")]
	public string[] CanDownloadRoles { get; set; }

	[JsonPropertyName("canRemoveLinkUids")]
	public object[] CanRemoveLinkUids { get; set; }

	[JsonPropertyName("canPrintRoles")]
	public string[] CanPrintRoles { get; set; }

	[JsonPropertyName("canViewContextMenuRoles")]
	public string[] CanViewContextMenuRoles { get; set; }

	[JsonPropertyName("canViewMetadataRoles")]
	public object[] CanViewMetadataRoles { get; set; }

	[JsonPropertyName("isEditFolderLink")]
	public bool IsEditFolderLink { get; set; }

	[JsonPropertyName("syncVarsByRoles")]
	public object SyncVarsByRoles { get; set; }
}

public partial class ShareToken
{
	[JsonPropertyName("itemId")]
	public object ItemId { get; set; }

	[JsonPropertyName("linkType")]
	public string LinkType { get; set; }

	[JsonPropertyName("linkKey")]
	public string LinkKey { get; set; }

	[JsonPropertyName("subPath")]
	public string SubPath { get; set; }

	[JsonPropertyName("secureHash")]
	public string SecureHash { get; set; }

	[JsonPropertyName("rlkey")]
	public object Rlkey { get; set; }
}

public partial class SharedLinkInfo
{
	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; }

	[JsonPropertyName("downloadTestUrl")]
	public Uri DownloadTestUrl { get; set; }

	[JsonPropertyName("hasPublicAudienceOrVisibility")]
	public bool HasPublicAudienceOrVisibility { get; set; }

	[JsonPropertyName("ownerName")]
	public string OwnerName { get; set; }

	[JsonPropertyName("ownerTeamLogo")]
	public object OwnerTeamLogo { get; set; }

	[JsonPropertyName("ownerTeamBackground")]
	public object OwnerTeamBackground { get; set; }

	[JsonPropertyName("ownerTeamName")]
	public object OwnerTeamName { get; set; }

	[JsonPropertyName("teamMemberBrandingPolicyEnabled")]
	public bool TeamMemberBrandingPolicyEnabled { get; set; }

	[JsonPropertyName("url")]
	public Uri Url { get; set; }
}

public partial class DropboxResult
{
	public static DropboxResult FromJson(string json) => JsonSerializer.Deserialize<DropboxResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this DropboxResult self) => JsonSerializer.Serialize(self, Converter.Settings);
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
