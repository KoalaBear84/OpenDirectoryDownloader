using Newtonsoft.Json;
using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.FileUpload;

public class FilesVc : IFileUploadSite
{
	public string Name => "Files.vc";

	public async Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path)
	{
		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries)
		{
			try
			{
				using FileStream fileStream = new(path, FileMode.Open);
				using StreamContent streamContent = new(fileStream);

				using MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}")
				{
					{ streamContent, "file", Path.GetFileName(path) }
				};

				using HttpResponseMessage httpResponseMessage = await httpClient.PostAsync("https://api.files.vc/upload", multipartFormDataContent);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string response = await httpResponseMessage.Content.ReadAsStringAsync();
					OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

					Program.Logger.Debug("Response from {siteName}: {response}", Name, OpenDirectoryIndexer.Session.UploadedUrlsResponse);

					return JsonConvert.DeserializeObject<FilesVcFile>(response);
				}
				else
				{
					Program.Logger.Error("Error uploading file, retry in 5 seconds..");
					await Task.Delay(TimeSpan.FromSeconds(5));
				}

				retries++;
			}
			catch (Exception ex)
			{
				retries++;
				Program.Logger.Error(ex, "Error uploading file, retry in 5 seconds..");
				await Task.Delay(TimeSpan.FromSeconds(5));
			}
		}

		throw new FriendlyException("Error uploading URLs");
	}
}

public class FilesVcFile : IFileUploadSiteFile
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("file_url")]
	public string FileUrl { get; set; }

	[JsonProperty("debug_info")]
	public FilesVCFileDebugInfo DebugInfo { get; set; }

	public string Url { get => FileUrl; }
}

public class FilesVCFileDebugInfo
{
	[JsonProperty("filename")]
	public string Filename { get; set; }

	[JsonProperty("filepath")]
	public string Filepath { get; set; }

	[JsonProperty("file_url")]
	public string FileUrl { get; set; }

	[JsonProperty("file_size")]
	public long FileSize { get; set; }

	[JsonProperty("hash")]
	public string Hash { get; set; }
}
