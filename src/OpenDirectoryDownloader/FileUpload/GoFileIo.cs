using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenDirectoryDownloader.Models;

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
				string jsonServer = await httpClient.GetStringAsync("https://apiv2.gofile.io/getServer");

				JObject result = JObject.Parse(jsonServer);

				if (result["status"].Value<string>() == "error")
				{
					throw new Exception("GoFile.io error, probably in maintenance");
				}

				string server = result.SelectToken("data.server").Value<string>();
				using FileStream fileStream = new(path, FileMode.Open);
				using StreamContent streamContent = new(fileStream);

				using MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}")
				{
					{ streamContent, "file", Path.GetFileName(path) }
				};

				using HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"https://{server}.gofile.io/uploadFile", multipartFormDataContent);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string response = await httpResponseMessage.Content.ReadAsStringAsync();
					OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

					Program.Logger.Debug("Response from {siteName}: {response}", Name, response);

					return JsonConvert.DeserializeObject<GoFileIoFile>(response);
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
	public string Url => $"https://gofile.io/?c={Data.Code}";

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("data")]
	public GoFileIoFileData Data { get; set; }
}

public class GoFileIoFileData
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("removalCode")]
	public string RemovalCode { get; set; }
}
