using OpenDirectoryDownloader.Parsing.Sites.AmazonS3;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>The bucket listing used to be read with XmlSerializer (which trimming can break); it must read the same.</summary>
public class AmazonS3ResultTests
{
	[Fact]
	public void Parse_ReadsAListBucketResult_InTheS3Namespace()
	{
		const string xml =
			"""
			<?xml version="1.0" encoding="UTF-8"?>
			<ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
				<Name>bucket</Name>
				<Prefix>photos/</Prefix>
				<Marker></Marker>
				<NextMarker>photos/b.jpg</NextMarker>
				<MaxKeys>1000</MaxKeys>
				<IsTruncated>true</IsTruncated>
				<Contents>
					<Key>photos/a.jpg</Key>
					<LastModified>2009-10-12T17:50:30.000Z</LastModified>
					<ETag>"fba9dede5f27731c9771645a39863328"</ETag>
					<Size>434234</Size>
					<StorageClass>STANDARD</StorageClass>
				</Contents>
				<Contents>
					<Key>photos/b.jpg</Key>
					<Size>12</Size>
				</Contents>
				<CommonPrefixes>
					<Prefix>photos/2006/</Prefix>
				</CommonPrefixes>
			</ListBucketResult>
			""";

		AmazonS3Result result = AmazonS3Result.Parse(new StringReader(xml));

		Assert.Equal("bucket", result.Name);
		Assert.Equal("photos/", result.Prefix);
		Assert.Equal(string.Empty, result.Marker);
		Assert.Equal("photos/b.jpg", result.NextMarker);
		Assert.Equal(1000, result.MaxKeys);
		Assert.True(result.IsTruncated);
		Assert.Equal(["photos/a.jpg", "photos/b.jpg"], result.Contents.Select(content => content.Key));
		Assert.Equal([434234L, 12L], result.Contents.Select(content => content.Size));
		Assert.Equal(new DateTime(2009, 10, 12, 17, 50, 30, DateTimeKind.Utc), result.Contents[0].LastModified);
		Assert.Equal("photos/2006/", Assert.Single(result.CommonPrefixes).Prefix);
	}

	[Fact]
	public void Parse_WithoutNamespaceOrPrefixes_GivesEmptyLists()
	{
		AmazonS3Result result = AmazonS3Result.Parse(new StringReader("<ListBucketResult><Name>b</Name><IsTruncated>false</IsTruncated></ListBucketResult>"));

		Assert.False(result.IsTruncated);
		Assert.Null(result.NextMarker);
		Assert.Empty(result.Contents);
		Assert.Empty(result.CommonPrefixes);
	}
}
