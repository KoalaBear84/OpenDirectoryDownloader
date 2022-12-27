using OpenDirectoryDownloader.Shared.Models;
using System.Net;
using System.Xml;
using System.Xml.Serialization;

namespace OpenDirectoryDownloader.Site.AmazonS3;

public static class AmazonS3Parser
{
	private const string Parser = "AmazonS3";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, bool hasHeader)
	{
		try
		{
			UrlEncodingParser urlEncodingParser = new(webDirectory.Url);

			string prefix = urlEncodingParser.AllKeys.Contains("prefix") ? urlEncodingParser["prefix"] : string.Empty;

			webDirectory = await ScanAsync(httpClient, webDirectory, prefix, hasHeader);
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory, string prefix, bool hasHeader)
	{
		Program.Logger.Debug("Retrieving listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			string bucketName = webDirectory.Uri.Host.Replace(".s3.amazonaws.com", string.Empty);

			bool isTruncated = false;
			string nextMarker = string.Empty;

			do
			{
				string url = GetUrl(bucketName, prefix, nextMarker, hasHeader);
				HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url);

				httpResponseMessage.EnsureSuccessStatusCode();

				AmazonS3Result result;

				using TextReader textReader = new StreamReader(await httpResponseMessage.Content.ReadAsStreamAsync());
				using XmlTextReader xmlTextReader = new(textReader);

				xmlTextReader.Namespaces = false;
				XmlSerializer xmlSerializer = new(typeof(AmazonS3Result));
				result = (AmazonS3Result)xmlSerializer.Deserialize(xmlTextReader);

				isTruncated = result.IsTruncated;
				nextMarker = result.NextMarker;

				// Like directories
				foreach (CommonPrefix commonPrefix in result.CommonPrefixes)
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = GetUrl(bucketName, commonPrefix.Prefix, nextMarker, hasHeader),
						Name = commonPrefix.Prefix
					});
				}

				// Like files
				foreach (Content content in result.Contents)
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = GetFileUrl(bucketName, hasHeader, content.Key),
						FileName = Path.GetFileName(content.Key),
						FileSize = content.Size
					});
				}
			} while (isTruncated);
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error processing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			//throw;
		}

		return webDirectory;
	}

	private static string GetFileUrl(string bucketName, bool amazons3header, string contentKey)
	{
		string domain = GetDomain(bucketName, amazons3header);

		return $"https://{domain}/{contentKey}";
	}

	private static string GetUrl(string bucketName, string prefix, string nextMarker, bool amazons3header)
	{
		string domain = GetDomain(bucketName, amazons3header);

		string url = $"https://{domain}/?delimiter=/&prefix={prefix}";

		if (!string.IsNullOrEmpty(nextMarker))
		{
			url += $"&marker={WebUtility.UrlEncode(nextMarker)}";
		}

		return url;
	}

	private static string GetDomain(string bucketName, bool amazons3header)
	{
		string domain = $"{bucketName}.{Constants.AmazonS3Domain}";

		if (!amazons3header)
		{
			domain = bucketName;
		}

		return domain;
	}
}
