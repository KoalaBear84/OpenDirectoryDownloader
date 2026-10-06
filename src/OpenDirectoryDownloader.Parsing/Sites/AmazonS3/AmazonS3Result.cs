using System.Xml;
using System.Xml.Linq;

namespace OpenDirectoryDownloader.Parsing.Sites.AmazonS3;

/// <summary>
/// https://docs.aws.amazon.com/AmazonS3/latest/API/API_ListObjects.html
/// </summary>
public class AmazonS3Result
{
	public string Name { get; set; }

	public string Prefix { get; set; }

	public int MaxKeys { get; set; }

	public bool IsTruncated { get; set; }

	public string Marker { get; set; }

	public string NextMarker { get; set; }

	public List<Content> Contents { get; set; } = [];

	public List<CommonPrefix> CommonPrefixes { get; set; } = [];

	/// <summary>
	/// Reads a ListBucketResult like XmlSerializer did (which trimming can break): elements by their local name,
	/// whatever namespace the bucket answers with, values with XmlConvert, missing elements keep their default.
	/// </summary>
	public static AmazonS3Result Parse(TextReader textReader)
	{
		XElement root = XDocument.Load(textReader).Root;

		if (root?.Name.LocalName != "ListBucketResult")
		{
			throw new InvalidOperationException($"Expected a ListBucketResult, got '{root?.Name.LocalName}'");
		}

		AmazonS3Result result = new()
		{
			Name = Value(root, "Name"),
			Prefix = Value(root, "Prefix"),
			Marker = Value(root, "Marker"),
			NextMarker = Value(root, "NextMarker")
		};

		if (Value(root, "MaxKeys") is string maxKeys)
		{
			result.MaxKeys = XmlConvert.ToInt32(maxKeys);
		}

		if (Value(root, "IsTruncated") is string isTruncated)
		{
			result.IsTruncated = XmlConvert.ToBoolean(isTruncated);
		}

		foreach (XElement element in root.Elements())
		{
			switch (element.Name.LocalName)
			{
				case "Contents":
					Content content = new()
					{
						Key = Value(element, "Key"),
						ETag = Value(element, "ETag"),
						StorageClass = Value(element, "StorageClass")
					};

					if (Value(element, "LastModified") is string lastModified)
					{
						content.LastModified = XmlConvert.ToDateTime(lastModified, XmlDateTimeSerializationMode.RoundtripKind);
					}

					if (Value(element, "Size") is string size)
					{
						content.Size = XmlConvert.ToInt64(size);
					}

					result.Contents.Add(content);
					break;
				case "CommonPrefixes":
					result.CommonPrefixes.Add(new CommonPrefix { Prefix = Value(element, "Prefix") });
					break;
			}
		}

		return result;
	}

	private static string Value(XElement parent, string localName) => parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;
}

public class Content
{
	public string Key { get; set; }

	public DateTime LastModified { get; set; }

	public string ETag { get; set; }

	public long Size { get; set; }

	public string StorageClass { get; set; }
}

public class CommonPrefix
{
	public string Prefix { get; set; }
}
