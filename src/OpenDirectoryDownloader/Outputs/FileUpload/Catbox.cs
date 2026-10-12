using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Scanning;

namespace OpenDirectoryDownloader.Outputs.FileUpload;

public class Catbox : IFileUploadSite
{
	public string Name => "Catbox.moe";

	public long? MaxFileSize => 200L * 1000 * 1000;

	public async Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path, ScanSession session, ILogger logger)
	{
		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries)
		{
			try
			{
				using FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);
				using StreamContent streamContent = new(new UploadProgressStream(fileStream, Name));

				using MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}")
				{
					{ new StringContent("fileupload"), "reqtype" },
					{ streamContent, "fileToUpload", Path.GetFileName(path) }
				};

				using HttpRequestMessage request = new(HttpMethod.Post, "https://catbox.moe/user/api.php") { Content = multipartFormDataContent };
				request.Headers.UserAgent.ParseAdd(Constants.UserAgent.Curl);

				using HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(request);
				string response = (await httpResponseMessage.Content.ReadAsStringAsync()).Trim();
				session.UploadedUrlsResponse = response;

				logger.LogDebug("Response from {siteName}: {response}", Name, response);

				if (httpResponseMessage.IsSuccessStatusCode && response.StartsWith("https://"))
				{
					return new CatboxFile { Url = response };
				}

				logger.LogError("Error uploading file ({response}), retry in 5 seconds..", response);
				await Task.Delay(TimeSpan.FromSeconds(5));
				retries++;
			}
			catch (Exception ex)
			{
				retries++;
				logger.LogError(ex, "Error uploading file, retry in 5 seconds..");
				await Task.Delay(TimeSpan.FromSeconds(5));
			}
		}

		throw new FriendlyException("Error uploading URLs");
	}
}

public class CatboxFile : IFileUploadSiteFile
{
	public string Url { get; set; }
}
