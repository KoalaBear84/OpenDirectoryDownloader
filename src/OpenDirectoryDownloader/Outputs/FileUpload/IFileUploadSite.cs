using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Scanning;
namespace OpenDirectoryDownloader.Outputs.FileUpload;

public interface IFileUploadSite
{
	string Name { get; }

	/// <summary>The largest file the site accepts in bytes, null when unknown or unlimited</summary>
	long? MaxFileSize { get; }

	Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path, ScanSession session, ILogger logger);
}
