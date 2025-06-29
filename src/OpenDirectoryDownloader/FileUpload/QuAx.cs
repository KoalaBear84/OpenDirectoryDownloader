using OpenDirectoryDownloader.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.FileUpload;

public class QuAx : IFileUploadSite
{
	public string Name => "qu.ax";

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
					{ streamContent, "files[]", Path.GetFileName(path) }
				};

				using HttpResponseMessage httpResponseMessage = await httpClient.PostAsync("https://qu.ax/upload.php?expiry=-1", multipartFormDataContent);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string response = await httpResponseMessage.Content.ReadAsStringAsync();
					OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

					Program.Logger.Debug("Response from {siteName}: {response}", Name, OpenDirectoryIndexer.Session.UploadedUrlsResponse);

					QuAxResponse quAxResponse = JsonSerializer.Deserialize<QuAxResponse>(response);

					if (quAxResponse == null || !quAxResponse.Success || quAxResponse.Files == null || quAxResponse.Files.Length == 0)
					{
						throw new FriendlyException("Error uploading file, no valid response received");
					}

					return quAxResponse;
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

public class QuAxResponse : IFileUploadSiteFile
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("files")]
	public QuAxFile[] Files { get; set; }

	public string Url => Files.First().Url;
}

public class QuAxFile
{
	[JsonPropertyName("hash")]
	public string Hash { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }

	[JsonPropertyName("size")]
	public int Size { get; set; }

	[JsonPropertyName("expiry")]
	public string Expiry { get; set; }
}
