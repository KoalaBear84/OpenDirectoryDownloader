using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using OpenDirectoryDownloader.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.FileUpload
{
    public class GoFileIo
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static async Task<GoFileIoFile> UploadFile(HttpClient httpClient, string path)
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

                    using (MultipartFormDataContent multipartFormDataContent = new MultipartFormDataContent($"Upload----{Guid.NewGuid()}"))
                    {
                        multipartFormDataContent.Add(new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path));

                        using (HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"https://{server}.gofile.io/uploadFile", multipartFormDataContent))
                        {
                            if (httpResponseMessage.IsSuccessStatusCode)
                            {
                                string response = await httpResponseMessage.Content.ReadAsStringAsync();

                                Logger.Debug($"Response from GoFile.io: {response}");

                                return JsonConvert.DeserializeObject<GoFileIoFile>(response);
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

    public class GoFileIoFile
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("data")]
        public GoFileIoFileData Data { get; set; }

        public string Url { get => $"https://gofile.io/?c={Data.Code}"; }
    }

    public class GoFileIoFileData
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("removalCode")]
        public string RemovalCode { get; set; }
    }
}
