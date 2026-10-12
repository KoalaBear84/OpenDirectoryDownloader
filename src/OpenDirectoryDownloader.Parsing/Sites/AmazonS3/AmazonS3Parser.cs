using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;
using System.Net;

namespace OpenDirectoryDownloader.Parsing.Sites.AmazonS3;

public static class AmazonS3Parser
{
	private const string Parser = "AmazonS3";

	/// <remarks>
	/// Errors are not handled here: an HTTP error (S3 answers 503 "Slow Down" when it is asked too fast) goes to the crawl
	/// engine, which retries with a wait and fewer threads, and reports the directory as failed when it gives up.
	/// </remarks>
	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, bool hasHeader)
	{
		UrlEncodingParser urlEncodingParser = new(webDirectory.Url);

		string prefix = urlEncodingParser.AllKeys.Contains("prefix") ? urlEncodingParser["prefix"] : string.Empty;

		return await ScanAsync(services, webDirectory, prefix, hasHeader);
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory, string prefix, bool hasHeader)
	{
		services.Logger.LogDebug("Retrieving listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		string bucketName = webDirectory.Uri.Host.Replace(".s3.amazonaws.com", string.Empty);

		// Only put on the directory when all pages are read: after a failure the whole directory is tried again
		List<WebDirectory> subdirectories = [];
		List<WebFile> files = [];

		bool isTruncated;
		string nextMarker = string.Empty;

		do
		{
			string url = GetUrl(bucketName, prefix, nextMarker, hasHeader);
			using HttpResponseMessage httpResponseMessage = await services.Http.GetAsync(url);

			httpResponseMessage.EnsureSuccessStatusCode();

			using TextReader textReader = new StreamReader(await httpResponseMessage.Content.ReadAsStreamAsync());
			AmazonS3Result result = AmazonS3Result.Parse(textReader);

			isTruncated = result.IsTruncated;
			nextMarker = result.NextMarker;

			// Like directories
			foreach (CommonPrefix commonPrefix in result.CommonPrefixes)
			{
				subdirectories.Add(new WebDirectory(webDirectory)
				{
					Parser = Parser,
					Url = GetUrl(bucketName, commonPrefix.Prefix, nextMarker, hasHeader),
					Name = commonPrefix.Prefix
				});
			}

			// Like files
			foreach (Content content in result.Contents)
			{
				files.Add(new WebFile
				{
					Url = GetFileUrl(bucketName, hasHeader, content.Key),
					FileName = Path.GetFileName(content.Key),
					FileSize = content.Size
				});
			}
		} while (isTruncated);

		webDirectory.Subdirectories.AddRange(subdirectories);
		webDirectory.Files.AddRange(files);

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
