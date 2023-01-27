using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Globalization;

namespace OpenDirectoryDownloader.Site.Dropbox;

public partial class DropboxResult
{
	[JsonProperty("entries")]
	public Entry[] Entries { get; set; }

	[JsonProperty("share_tokens")]
	public ShareToken[] ShareTokens { get; set; }

	[JsonProperty("shared_link_infos")]
	public SharedLinkInfo[] SharedLinkInfos { get; set; }

	[JsonProperty("share_permissions")]
	public SharePermission[] SharePermissions { get; set; }

	[JsonProperty("takedown_request_type")]
	public object TakedownRequestType { get; set; }

	[JsonProperty("total_num_entries")]
	public long TotalNumEntries { get; set; }

	[JsonProperty("has_more_entries")]
	public bool HasMoreEntries { get; set; }

	[JsonProperty("next_request_voucher")]
	public string NextRequestVoucher { get; set; }

	[JsonProperty("folder")]
	public Folder Folder { get; set; }

	[JsonProperty("folder_share_permission")]
	public SharePermission FolderSharePermission { get; set; }

	[JsonProperty("folder_share_token")]
	public ShareToken FolderShareToken { get; set; }

	[JsonProperty("folder_shared_link_info")]
	public SharedLinkInfo FolderSharedLinkInfo { get; set; }
}

public partial class Entry
{
	[JsonProperty("bytes")]
	public long Bytes { get; set; }

	[JsonProperty("file_id")]
	public string FileId { get; set; }

	[JsonProperty("filename")]
	public string Filename { get; set; }

	[JsonProperty("href")]
	public Uri Href { get; set; }

	[JsonProperty("icon")]
	public string Icon { get; set; }

	[JsonProperty("is_dir")]
	public bool IsDir { get; set; }

	[JsonProperty("ns_id")]
	public long NsId { get; set; }

	[JsonProperty("open_in_app_data")]
	public object OpenInAppData { get; set; }

	[JsonProperty("preview")]
	public Preview Preview { get; set; }

	[JsonProperty("preview_type")]
	public string PreviewType { get; set; }

	[JsonProperty("revision_id")]
	public string RevisionId { get; set; }

	[JsonProperty("sjid")]
	public long Sjid { get; set; }

	[JsonProperty("sort_key")]
	public string[] SortKey { get; set; }

	[JsonProperty("thumbnail_url_tmpl")]
	public Uri ThumbnailUrlTmpl { get; set; }

	[JsonProperty("ts")]
	public long Ts { get; set; }

	[JsonProperty("is_symlink")]
	public bool IsSymlink { get; set; }
}

public partial class Preview
{
	[JsonProperty("content")]
	public Content Content { get; set; }

	[JsonProperty("preview_url")]
	public Uri PreviewUrl { get; set; }
}

public partial class Content
{
	[JsonProperty(".tag")]
	public string Tag { get; set; }

	[JsonProperty("text_url_tmpl")]
	public Uri TextUrlTmpl { get; set; }

	[JsonProperty("image_url_tmpl")]
	public Uri ImageUrlTmpl { get; set; }

	[JsonProperty("refresh_url")]
	public Uri RefreshUrl { get; set; }

	[JsonProperty("placeholder_image_url")]
	public Uri PlaceholderImageUrl { get; set; }

	[JsonProperty("autoprint_url")]
	public Uri AutoprintUrl { get; set; }

	[JsonProperty("supported_widths")]
	public long[] SupportedWidths { get; set; }
}

public partial class Folder
{
	[JsonProperty("_mount_access_perms")]
	public string[] MountAccessPerms { get; set; }

	[JsonProperty("filename")]
	public string Filename { get; set; }

	[JsonProperty("href")]
	public Uri Href { get; set; }

	[JsonProperty("is_dir")]
	public bool IsDir { get; set; }

	[JsonProperty("open_in_app_data")]
	public object OpenInAppData { get; set; }

	[JsonProperty("shared_folder_id")]
	public object SharedFolderId { get; set; }

	[JsonProperty("ns_id")]
	public long NsId { get; set; }

	[JsonProperty("sort_key")]
	public string[] SortKey { get; set; }

	[JsonProperty("folder_id")]
	public string FolderId { get; set; }
}

public partial class SharePermission
{
	[JsonProperty("canCopyToDropboxRoles")]
	public string[] CanCopyToDropboxRoles { get; set; }

	[JsonProperty("canSyncToDropboxRoles")]
	public object[] CanSyncToDropboxRoles { get; set; }

	[JsonProperty("canDownloadRoles")]
	public string[] CanDownloadRoles { get; set; }

	[JsonProperty("canRemoveLinkUids")]
	public object[] CanRemoveLinkUids { get; set; }

	[JsonProperty("canPrintRoles")]
	public string[] CanPrintRoles { get; set; }

	[JsonProperty("canViewContextMenuRoles")]
	public string[] CanViewContextMenuRoles { get; set; }

	[JsonProperty("canViewMetadataRoles")]
	public object[] CanViewMetadataRoles { get; set; }

	[JsonProperty("isEditFolderLink")]
	public bool IsEditFolderLink { get; set; }

	[JsonProperty("syncVarsByRoles")]
	public object SyncVarsByRoles { get; set; }
}

public partial class ShareToken
{
	[JsonProperty("itemId")]
	public object ItemId { get; set; }

	[JsonProperty("linkType")]
	public string LinkType { get; set; }

	[JsonProperty("linkKey")]
	public string LinkKey { get; set; }

	[JsonProperty("subPath")]
	public string SubPath { get; set; }

	[JsonProperty("secureHash")]
	public string SecureHash { get; set; }

	[JsonProperty("rlkey")]
	public object Rlkey { get; set; }
}

public partial class SharedLinkInfo
{
	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("downloadTestUrl")]
	public Uri DownloadTestUrl { get; set; }

	[JsonProperty("hasPublicAudienceOrVisibility")]
	public bool HasPublicAudienceOrVisibility { get; set; }

	[JsonProperty("ownerName")]
	public string OwnerName { get; set; }

	[JsonProperty("ownerTeamLogo")]
	public object OwnerTeamLogo { get; set; }

	[JsonProperty("ownerTeamBackground")]
	public object OwnerTeamBackground { get; set; }

	[JsonProperty("ownerTeamName")]
	public object OwnerTeamName { get; set; }

	[JsonProperty("teamMemberBrandingPolicyEnabled")]
	public bool TeamMemberBrandingPolicyEnabled { get; set; }

	[JsonProperty("url")]
	public Uri Url { get; set; }
}

public partial class DropboxResult
{
	public static DropboxResult FromJson(string json) => JsonConvert.DeserializeObject<DropboxResult>(json, Converter.Settings);
}

public static class Serialize
{
	public static string ToJson(this DropboxResult self) => JsonConvert.SerializeObject(self, Converter.Settings);
}

internal static class Converter
{
	public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
	{
		MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
		Converters =
		{
			new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
		},
	};
}
