using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Scanning;
namespace OpenDirectoryDownloader.Outputs.FileUpload;

public interface IFileUploadSite
{
	string Name { get; }
	Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path, ScanSession session, ILogger logger);
}
