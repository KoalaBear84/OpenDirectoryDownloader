using Newtonsoft.Json;
using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.FileUpload;

public class AnonFiles : IFileUploadSite
{
	public string Name => "AnonFiles.com";

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

				using HttpResponseMessage httpResponseMessage = await httpClient.PostAsync("https://api.anonfiles.com/upload", multipartFormDataContent);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string response = await httpResponseMessage.Content.ReadAsStringAsync();
					OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

					Program.Logger.Debug("Response from {siteName}: {response}", Name, OpenDirectoryIndexer.Session.UploadedUrlsResponse);

					return JsonConvert.DeserializeObject<AnonFilesFile>(response);
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

public class AnonFilesFile : IFileUploadSiteFile
{
	[JsonProperty("status")]
	public bool Status { get; set; }

	[JsonProperty("data")]
	public AnonFilesFileData Data { get; set; }

	public string Url { get => Data.File.Url.Short; }
}

public class AnonFilesFileData
{
	[JsonProperty("file")]
	public AnonFilesFileDataFile File { get; set; }
}

public class AnonFilesFileDataFile
{
	[JsonProperty("url")]
	public AnonFilesFileDataFileUrl Url { get; set; }

	[JsonProperty("metadata")]
	public AnonFilesFileDataFileMetadata Metadata { get; set; }
}

public class AnonFilesFileDataFileUrl
{
	[JsonProperty("full")]
	public string Full { get; set; }

	[JsonProperty("short")]
	public string Short { get; set; }
}

public class AnonFilesFileDataFileMetadata
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("size")]
	public AnonFilesFileDataFileMetadataSize Size { get; set; }
}

public class AnonFilesFileDataFileMetadataSize
{
	[JsonProperty("bytes")]
	public int Bytes { get; set; }

	[JsonProperty("readable")]
	public string Readable { get; set; }
}
