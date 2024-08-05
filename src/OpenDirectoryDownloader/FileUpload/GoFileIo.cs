using OpenDirectoryDownloader.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.FileUpload;

public class GoFileIo : IFileUploadSite
{
	public string Name => "GoFile.io";

	public async Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path)
	{
		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries)
		{
			try
			{
				GoFileIoServersResponse serversResponse = await httpClient.GetFromJsonAsync<GoFileIoServersResponse>("https://api.gofile.io/servers");

				if (serversResponse.Status != "ok")
				{
					throw new Exception("GoFile.io servers error, in maintenance?");
				}

				string server = serversResponse.Data.Servers.First().Name;

				using HttpResponseMessage httpResponseMessageAccount = await httpClient.PostAsync("https://api.gofile.io/accounts", new StringContent("{}", Encoding.UTF8, "text/plain"));

				GoFileIoAccountsResponse accountsResponse = await httpResponseMessageAccount.Content.ReadFromJsonAsync<GoFileIoAccountsResponse>();

				if (accountsResponse.Status != "ok")
				{
					throw new Exception("GoFile.io accounts error, in maintenance?");
				}

				httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountsResponse.Data.Token);

				GoFileIoAccountResponse accountResponse = await httpClient.GetFromJsonAsync<GoFileIoAccountResponse>($"https://api.gofile.io/accounts/{accountsResponse.Data.Id}");

				if (accountResponse.Status != "ok")
				{
					throw new Exception("GoFile.io account error, in maintenance?");
				}

				using HttpResponseMessage httpResponseMessageCreateFolder = await httpClient.PostAsync("https://api.gofile.io/contents/createfolder", new StringContent($"{{\"parentFolderId\": \"{accountsResponse.Data.RootFolder}\"}}", Encoding.UTF8, "application/json"));

				GoFileIoFolderResponse createFolderResponse = await httpResponseMessageCreateFolder.Content.ReadFromJsonAsync<GoFileIoFolderResponse>();

				if (createFolderResponse.Status != "ok")
				{
					throw new Exception("GoFile.io create folder error, in maintenance?");
				}

				using HttpResponseMessage httpResponseMessageUpdateFolder = await httpClient.PutAsync($"https://api.gofile.io/contents/{createFolderResponse.Data.Id}/update", new StringContent("{\"attribute\": \"public\", \"attributeValue\": \"true\"}", Encoding.UTF8, "application/json"));

				GoFileIoFolderResponse updateFolderResponse = await httpResponseMessageUpdateFolder.Content.ReadFromJsonAsync<GoFileIoFolderResponse>();

				if (updateFolderResponse.Status != "ok")
				{
					throw new Exception("GoFile.io update folder error, in maintenance?");
				}

				using FileStream fileStream = new(path, FileMode.Open);
				using StreamContent streamContent = new(fileStream);

				using MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}")
				{
					{ new StringContent(createFolderResponse.Data.Id), "folderId" },
					{ streamContent, "file", Path.GetFileName(path) }
				};

				using HttpResponseMessage httpResponseMessageUploadFile = await httpClient.PostAsync($"https://{server}.gofile.io/contents/uploadFile", multipartFormDataContent);

				if (httpResponseMessageUploadFile.IsSuccessStatusCode)
				{
					GoFileIoUploadFileResponse uploadFileResponse = await httpResponseMessageUploadFile.Content.ReadFromJsonAsync<GoFileIoUploadFileResponse>();
					OpenDirectoryIndexer.Session.UploadedUrlsResponse = JsonSerializer.Serialize(uploadFileResponse);

					Program.Logger.Debug("Response from {siteName}: {response}", Name, uploadFileResponse);

					return JsonSerializer.Deserialize<GoFileIoFile>(JsonSerializer.Serialize(uploadFileResponse));
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

public class GoFileIoFile : IFileUploadSiteFile
{
	public string Url => Data.DownloadPage;

	[JsonPropertyName("status")]
	public string Status { get; set; }

	[JsonPropertyName("data")]
	public GoFileIoUploadedFile Data { get; set; }
}

public class GoFileIoFileData
{
	[JsonPropertyName("code")]
	public string Code { get; set; }

	[JsonPropertyName("removalCode")]
	public string RemovalCode { get; set; }
}

public class GoFileIoResponse
{
	[JsonPropertyName("status")]
	public string Status { get; set; }
}

public class GoFileIoServersResponse : GoFileIoResponse
{
	[JsonPropertyName("data")]
	public GoFileIoServersData Data { get; set; }
}

public class GoFileIoServersData
{
	[JsonPropertyName("servers")]
	public List<GoFileIoServer> Servers { get; set; }
}

public class GoFileIoServer
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("zone")]
	public string Zone { get; set; }
}

public class GoFileIoFolderResponse : GoFileIoResponse
{
	[JsonPropertyName("data")]
	public GoFileIoFolder Data { get; set; }
}

public class GoFileIoFolder
{
	[JsonPropertyName("code")]
	public string Code { get; set; }

	[JsonPropertyName("createTime")]
	public int CreateTime { get; set; }

	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("modTime")]
	public int ModTime { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("owner")]
	public string Owner { get; set; }

	[JsonPropertyName("parentFolder")]
	public string ParentFolder { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }
}

public class GoFileIoAccountsResponse : GoFileIoResponse
{
	[JsonPropertyName("data")]
	public GoFileIoAccount Data { get; set; }
}

public class GoFileIoAccount
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("email")]
	public string Email { get; set; }

	[JsonPropertyName("rootFolder")]
	public string RootFolder { get; set; }

	[JsonPropertyName("tier")]
	public string Tier { get; set; }

	[JsonPropertyName("token")]
	public string Token { get; set; }

	[JsonPropertyName("statsCurrent")]
	public GoFileIoAccountStats StatsCurrent { get; set; }
}

public class GoFileIoAccountResponse : GoFileIoResponse
{
	[JsonPropertyName("data")]
	public GoFileIoAccount Data { get; set; }
}

public class GoFileIoAccountStats
{
	[JsonPropertyName("folderCount")]
	public int FolderCount { get; set; }

	[JsonPropertyName("fileCount")]
	public int FileCount { get; set; }

	[JsonPropertyName("storage")]
	public int Storage { get; set; }
}

public class GoFileIoUploadFileResponse : IFileUploadSiteFile
{
	[JsonPropertyName("data")]
	public GoFileIoUploadedFile Data { get; set; }

	public string Url => Data.DownloadPage;
}

public class GoFileIoUploadedFile
{
	[JsonPropertyName("createTime")]
	public int CreateTime { get; set; }

	[JsonPropertyName("downloadPage")]
	public string DownloadPage { get; set; }

	[JsonPropertyName("guestToken")]
	public string GuestToken { get; set; }

	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("md5")]
	public string Md5 { get; set; }

	[JsonPropertyName("mimetype")]
	public string MimeType { get; set; }

	[JsonPropertyName("modTime")]
	public int ModTime { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("parentFolder")]
	public string ParentFolder { get; set; }

	[JsonPropertyName("parentFolderCode")]
	public string ParentFolderCode { get; set; }

	[JsonPropertyName("servers")]
	public List<string> Servers { get; set; }

	[JsonPropertyName("size")]
	public int Size { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; }
}
