using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.FileUpload
{
	public interface IFileUploadSite
	{
		string Name { get; }
		Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path);
	}
}