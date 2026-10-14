using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Reporting;
using OpenDirectoryDownloader.Scanning;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>What failed during a scan is noted in the Reddit table, and no row (or empty cell) is there when nothing did</summary>
public class StatisticsErrorRowTests
{
	private static ScanSession Session() => new(new WebDirectory(null) { Url = "http://localhost/", Name = Constants.Root });

	[Fact]
	public void NothingFailed_HasNoErrorRow()
	{
		Assert.DoesNotContain("**Errors:**", Statistics.GetSessionStats(Session(), onlyRedditStats: true));
	}

	[Fact]
	public void DirectoryErrors_AreCounted()
	{
		ScanSession session = Session();
		session.ReportError("http://localhost/a/");
		session.ReportError("http://localhost/b/");

		string stats = Statistics.GetSessionStats(session, onlyRedditStats: true);

		Assert.Contains(@"|**Errors:** 2 directories failed|\-|\-|", stats);
	}

	[Fact]
	public void FailedUpload_IsNoted_WithoutEmptyCells()
	{
		ScanSession session = Session();
		session.UploadFailed = true;

		string stats = Statistics.GetSessionStats(session, onlyRedditStats: true);

		Assert.Contains(@"|\-|\-|**Urls file:** Upload failed|", stats);
	}
}
