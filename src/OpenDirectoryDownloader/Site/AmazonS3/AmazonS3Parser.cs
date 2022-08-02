using NLog;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace OpenDirectoryDownloader.Site.AmazonS3;

public static class AmazonS3Parser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private const string Parser = "AmazonS3";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			UrlEncodingParser urlEncodingParser = new UrlEncodingParser(webDirectory.Url);

			string prefix = urlEncodingParser.AllKeys.Contains("prefix") ? urlEncodingParser["prefix"] : string.Empty;

			webDirectory = await ScanAsync(httpClient, webDirectory, prefix);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error parsing {Parser} for URL: {webDirectory.Url}");
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

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory, string prefix)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

		webDirectory.Parser = Parser;

		try
		{
			string bucketName = webDirectory.Uri.Host.Replace(".s3.amazonaws.com", string.Empty);

			bool isTruncated = false;
			string nextMarker = string.Empty;

			do
			{
				string url = GetUrl(bucketName, prefix, nextMarker);
				HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url);

				httpResponseMessage.EnsureSuccessStatusCode();

				AmazonS3Result result;

				using (TextReader textReader = new StreamReader(await httpResponseMessage.Content.ReadAsStreamAsync()))
				{
					using (XmlTextReader xmlTextReader = new XmlTextReader(textReader))
					{
						xmlTextReader.Namespaces = false;
						XmlSerializer xmlSerializer = new XmlSerializer(typeof(AmazonS3Result));
						result = (AmazonS3Result)xmlSerializer.Deserialize(xmlTextReader);
					}
				}

				isTruncated = result.IsTruncated;
				nextMarker = result.NextMarker;

				// Like directories
				foreach (CommonPrefix commonPrefix in result.CommonPrefixes)
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = GetUrl(bucketName, commonPrefix.Prefix, nextMarker),
						Name = commonPrefix.Prefix
					});
				}

				// Like files
				foreach (Content content in result.Contents)
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = $"https://{bucketName}.{Constants.AmazonS3Domain}/{content.Key}",
						FileName = Path.GetFileName(content.Key),
						FileSize = content.Size
					});
				}
			} while (isTruncated);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error processing {Parser} for URL: {webDirectory.Url}");
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

	private static string GetUrl(string bucketName, string prefix, string nextMarker)
	{
		string url = $"https://{bucketName}.{Constants.AmazonS3Domain}/?delimiter=%2F&prefix={WebUtility.UrlEncode(prefix)}";

		if (!string.IsNullOrEmpty(nextMarker))
		{
			url += $"&marker={WebUtility.UrlEncode(nextMarker)}";
		}

		return url;
	}
}
