using Newtonsoft.Json;
using NLog;
using OpenDirectoryDownloader.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.FileUpload
{
    public class FileIo
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static async Task<FileIoFile> UploadFile(HttpClient httpClient, string path)
        {
            int retries = 0;
            int maxRetries = 5;

            while (retries < maxRetries)
            {
                try
                {
                    using (MultipartFormDataContent multipartFormDataContent = new MultipartFormDataContent($"Upload----{Guid.NewGuid()}"))
                    {
                        multipartFormDataContent.Add(new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path));

                        using (HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"https://file.io/?expires=1m", multipartFormDataContent))
                        {
                            if (httpResponseMessage.IsSuccessStatusCode)
                            {
                                string response = await httpResponseMessage.Content.ReadAsStringAsync();

                                Logger.Debug($"Response from File.io: {response}");

                                return JsonConvert.DeserializeObject<FileIoFile>(response);
                            }
                            else
                            {
                                retries++;
                                Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
                                await Task.Delay(TimeSpan.FromSeconds(5));
                            }
                        }
                    }
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

    public class FileIoFile
    {
        [JsonProperty("success")]
        public string Success { get; set; }

        [JsonProperty("error")]
        public int Error { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("link")]
        public string Url { get; set; }

        [JsonProperty("expiry")]
        public string Expiry { get; set; }
    }
}
