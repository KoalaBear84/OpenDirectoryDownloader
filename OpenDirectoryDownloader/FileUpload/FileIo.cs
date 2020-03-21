using Newtonsoft.Json;
using NLog;
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
            using (MultipartFormDataContent multipartFormDataContent = new MultipartFormDataContent($"Upload----{Guid.NewGuid()}"))
            {
                multipartFormDataContent.Add(new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path));

                int i = 0;
                int retries = 5;

                while (i < retries)
                {
                    try
                    {
                        using (HttpResponseMessage httpResponseMessage = await httpClient.PostAsync("https://file.io/?expires=6m", multipartFormDataContent))
                        {
                            if (httpResponseMessage.IsSuccessStatusCode)
                            {
                                string response = await httpResponseMessage.Content.ReadAsStringAsync();

                                Logger.Debug($"Response from File.io: {response}");

                                return JsonConvert.DeserializeObject<FileIoFile>(response);
                            }
                            else
                            {
                                Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
                                await Task.Delay(TimeSpan.FromSeconds(5));
                            }

                            retries++;
                        }
                    }
                    catch (Exception)
                    {
                        Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }

                throw new Exception("Error uploading Urls file...");
            }
        }
    }

    public class FileIoFile
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("link")]
        public Uri Link { get; set; }

        [JsonProperty("expiry")]
        public string Expiry { get; set; }
    }
}
