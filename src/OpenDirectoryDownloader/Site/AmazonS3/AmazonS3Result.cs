using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace OpenDirectoryDownloader.Site.AmazonS3;

[XmlRoot(Namespace = "http://s3.amazonaws.com/doc/2006-03-01/", ElementName = "ListBucketResult")]
public class AmazonS3Result
{
	[XmlElement("Name")]
	public string Name { get; set; }

	[XmlElement("Prefix")]
	public string Prefix { get; set; }

	[XmlElement("MaxKeys")]
	public int MaxKeys { get; set; }

	[XmlElement("IsTruncated")]
	public bool IsTruncated { get; set; }

	[XmlElement("Marker")]
	public string Marker { get; set; }

	[XmlElement("NextMarker")]
	public string NextMarker { get; set; }

	[XmlElement(ElementName = "Contents")]
	public List<Content> Contents { get; set; }

	[XmlElement(ElementName = "CommonPrefixes")]
	public List<CommonPrefix> CommonPrefixes { get; set; }
}

public class Content
{
	[XmlElement("Key")]
	public string Key { get; set; }

	[XmlElement("LastModified")]
	public DateTime LastModified { get; set; }

	[XmlElement("ETag")]
	public string ETag { get; set; }

	[XmlElement("Size")]
	public long Size { get; set; }

	[XmlElement("StorageClass")]
	public string StorageClass { get; set; }
}

public class CommonPrefix
{
	[XmlElement("Prefix")]
	public string Prefix { get; set; }
}
