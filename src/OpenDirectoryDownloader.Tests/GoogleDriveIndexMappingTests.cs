using OpenDirectoryDownloader.Site.GDIndex;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class GoogleDriveIndexMappingTests
{
	[Theory]
	[InlineData("application/vnd.google-apps.document", "Doc")]
	[InlineData("application/vnd.google-apps.spreadsheet", "Sheet")]
	[InlineData("application/vnd.google-apps.presentation", "Slide")]
	[InlineData("application/vnd.google-apps.drawing", "Drawing")]
	[InlineData("application/vnd.google-apps.form", "Form")]
	public void GetFriendlyMimeTypeName_KnownGoogleNativeType_ReturnsFriendlyName(string mimeType, string expected)
	{
		Assert.Equal(expected, GoogleDriveIndexMapping.GetFriendlyMimeTypeName(mimeType));
	}

	[Theory]
	[InlineData("application/pdf")]
	[InlineData("video/mp4")]
	[InlineData("application/vnd.google-apps.folder")]
	[InlineData(null)]
	public void GetFriendlyMimeTypeName_NotAKnownGoogleNativeType_ReturnsNull(string mimeType)
	{
		Assert.Null(GoogleDriveIndexMapping.GetFriendlyMimeTypeName(mimeType));
	}
}
