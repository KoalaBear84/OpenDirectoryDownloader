using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.FileUpload;

public class AnonFiles : IFileUploadSite
{
	public string Name => "AnonFiles.com";

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	public async Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path)
	{
		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries)
		{
			try
			{
				using (MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}"))
				{
					multipartFormDataContent.Add(new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path));

					using (HttpResponseMessage httpResponseMessage = await httpClient.PostAsync("https://api.anonfiles.com/upload", multipartFormDataContent))
					{
						if (httpResponseMessage.IsSuccessStatusCode)
						{
							string response = await httpResponseMessage.Content.ReadAsStringAsync();
							OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

							Logger.Debug($"Response from {Name}: {response}");

							return JsonConvert.DeserializeObject<AnonFilesFile>(response);
						}
						else
						{
							Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
							await Task.Delay(TimeSpan.FromSeconds(5));
						}
					}
				}

				retries++;
			}
			catch (Exception)
			{
				retries++;
				Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
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
