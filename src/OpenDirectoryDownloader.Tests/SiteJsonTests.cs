using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// The site responses are read with source generated JSON metadata (so the trimmed release keeps it): every type
/// must have it, with its own converters.
/// </summary>
public class SiteJsonTests
{
	private static readonly Dictionary<string, Func<string, string>> RoundTrips = new()
	{
		{ "AListListing", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.AList.ListResult.AListListing.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.AList.ListResult.Serialize.ToJson(value); } },
		{ "BlitzfilesTechResponse", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.BlitzfilesTech.BlitzfilesTechResponse.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.BlitzfilesTech.Serialize.ToJson(value); } },
		{ "CalibreResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Calibre.CalibreResult.CalibreResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Calibre.CalibreResult.Serialize.ToJson(value); } },
		{ "CalibreUpdate", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Calibre.CalibreUpdate.CalibreUpdate.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Calibre.CalibreUpdate.Serialize.ToJson(value); } },
		{ "CopypartyListing", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Copyparty.CopypartyListing.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Copyparty.Serialize.ToJson(value); } },
		{ "CrushFtpResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.CrushFtp.CrushFtpResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.CrushFtp.Serialize.ToJson(value); } },
		{ "DropboxResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Dropbox.DropboxResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Dropbox.Serialize.ToJson(value); } },
		{ "DufsListing", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Dufs.DufsListing.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Dufs.Serialize.ToJson(value); } },
		{ "FileBrowserResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.FileBrowser.FileBrowserResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.FileBrowser.Serialize.ToJson(value); } },
		{ "BhadooIndexResponse", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.Bhadoo.BhadooIndexResponse.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.Bhadoo.Serialize.ToJson(value); } },
		{ "GdIndexResponse", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.GdIndex.GdIndexResponse.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.GdIndex.Serialize.ToJson(value); } },
		{ "Go2IndexResponse", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.Go2Index.Go2IndexResponse.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.Go2Index.Serialize.ToJson(value); } },
		{ "GoIndexResponse", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.GoIndex.GoIndexResponse.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.GDIndex.GoIndex.Serialize.ToJson(value); } },
		{ "GitHubResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.GitHub.GitHubResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.GitHub.Serialize.ToJson(value); } },
		{ "GoFileIOListingResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.GoFileIO.GoFileIOListingResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.GoFileIO.Serialize.ToJson(value); } },
		{ "HfsListing", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.HFS.HfsListing.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.HFS.Serialize.ToJson(value); } },
		{ "MediafireResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Mediafire.MediafireResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Mediafire.Serialize.ToJson(value); } },
		{ "PCloudListing", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.PCloud.ListResult.PCloudListing.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.PCloud.ListResult.Serialize.ToJson(value); } },
		{ "PixeldrainFileResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.FileResult.PixeldrainFileResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.FileResult.Serialize.ToJson(value); } },
		{ "PixeldrainListResult", json => { var value = global::OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.ListResult.PixeldrainListResult.FromJson(json); return global::OpenDirectoryDownloader.Parsing.Sites.Pixeldrain.ListResult.Serialize.ToJson(value); } },
	};

	public static TheoryData<string> Types => [.. RoundTrips.Keys];

	[Theory]
	[MemberData(nameof(Types))]
	public void EverySiteResponse_HasSourceGeneratedMetadata(string type)
	{
		Assert.StartsWith("{", RoundTrips[type]("{}"));
	}
}
