using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.GoogleDrive;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;

namespace OpenDirectoryDownloader.Protocols;

/// <summary>Lists Google Drive folders through the Drive API, signing in on first use.</summary>
public sealed class GoogleDriveFetcher(ILogger<GoogleDriveIndexer> logger)
{
	private readonly SemaphoreSlim _lock = new(1, 1);
	private GoogleDriveIndexer _indexer;

	/// <summary>Lists the folder of <paramref name="webDirectory"/> into a new, detached directory.</summary>
	public async Task<WebDirectory> FetchAsync(WebDirectory webDirectory, CancellationToken cancellationToken)
	{
		if (webDirectory.Uri.Segments.Contains("folderview"))
		{
			UrlEncodingParser urlEncodingParserFolderView = new(webDirectory.Url);

			webDirectory.Url = $"https://drive.google.com/drive/folders/{urlEncodingParserFolderView["id"]}";
		}

		string baseUrl = webDirectory.Url;
		string resourceKey = new UrlEncodingParser(baseUrl)["resourcekey"];

		GoogleDriveIndexer indexer = await GetIndexerAsync(cancellationToken);

		WebDirectory result = await indexer.IndexAsync(new WebDirectory(webDirectory.ParentDirectory) { Url = webDirectory.Url, Name = webDirectory.Name }, resourceKey);
		result.Url = baseUrl;

		return result;
	}

	private async Task<GoogleDriveIndexer> GetIndexerAsync(CancellationToken cancellationToken)
	{
		if (_indexer is not null)
		{
			return _indexer;
		}

		await _lock.WaitAsync(cancellationToken);

		try
		{
			return _indexer ??= await GoogleDriveIndexer.CreateAsync(logger, cancellationToken);
		}
		finally
		{
			_lock.Release();
		}
	}
}
