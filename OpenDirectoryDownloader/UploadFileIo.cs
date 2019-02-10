using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    public class UploadFileIo
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static async Task<UploadFilesFile> UploadFile(HttpClient httpClient, string path)
        {
            using (MultipartFormDataContent multipartFormDataContent = new MultipartFormDataContent($"Upload----{Guid.NewGuid()}"))
            {
                multipartFormDataContent.Add(new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path));

                using (var message = await httpClient.PostAsync("https://up.uploadfiles.io/upload", multipartFormDataContent))
                {
                    string response = await message.Content.ReadAsStringAsync();

                    Logger.Debug($"Response from Uploadfiles.io: {response}");

                    return JsonConvert.DeserializeObject<UploadFilesFile>(response);
                }
            }
        }
    }

    public class UploadFilesFile
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("destination")]
        public Uri Destination { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("size")]
        public string Size { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("expiry")]
        public string Expiry { get; set; }

        [JsonProperty("session_id")]
        public string SessionId { get; set; }

        [JsonProperty("timing")]
        public string Timing { get; set; }
    }
}
