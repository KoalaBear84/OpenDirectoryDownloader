using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.FileUpload;

public class Pixeldrain : IFileUploadSite
{
	public string Name => "Pixeldrain.com";

	private readonly Logger Logger = LogManager.GetCurrentClassLogger();

	public async Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path)
	{
		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries)
		{
			try
			{
				using (HttpResponseMessage httpResponseMessage = await httpClient.PutAsync($"https://pixeldrain.com/api/file/{Uri.EscapeDataString(Path.GetFileName(path))}", new StreamContent(new FileStream(path, FileMode.Open))))
				{
					if (httpResponseMessage.IsSuccessStatusCode)
					{
						string response = await httpResponseMessage.Content.ReadAsStringAsync();
						Logger.Debug($"Response from {Name}: {response}");

						return JsonConvert.DeserializeObject<PixeldrainFile>(response);
					}
					else
					{
						Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
						await Task.Delay(TimeSpan.FromSeconds(5));
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

public class PixeldrainFile : IFileUploadSiteFile
{
	public string Url => $"https://pixeldrain.com/u/{Id}";

	[JsonProperty("id")]
	public string Id { get; set; }
}
