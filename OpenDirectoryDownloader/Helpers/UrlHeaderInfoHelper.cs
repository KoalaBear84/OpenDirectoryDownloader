using NLog;
using Polly;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Helpers
{
    public static class UrlHeaderInfoHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(4,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, span, retryCount, context) =>
                {
                    Logger.Warn($"Error {ex.Message} retrieving on try {retryCount} for url '{context}'. Waiting {span.TotalSeconds} seconds.");
                }
            );

        public static async Task<long?> GetUrlFileSizeAsync(this HttpClient httpClient, string url)
        {
            try
            {
                return await RetryPolicy.ExecuteAsync(async () =>
                {
                    return (await httpClient.SendAsync(new HttpRequestMessage
                    {
                        RequestUri = new Uri(url),
                        Method = HttpMethod.Head
                    }, HttpCompletionOption.ResponseHeadersRead)).Content?.Headers.ContentLength;
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error retrieving filesize for Url: '{url}'");

                return null;
            }
        }

        public static async Task<long?> GetUrlFileSizeByDownloadingAsync(this HttpClient httpClient, string url)
        {
            try
            {
                return await RetryPolicy.ExecuteAsync(async () =>
                {
                    return (await httpClient.SendAsync(new HttpRequestMessage
                    {
                        RequestUri = new Uri(url)
                    }, HttpCompletionOption.ResponseContentRead)).Content?.Headers.ContentLength;
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error retrieving filesize for Url: '{url}'");

                return null;
            }
        }

        public static async Task<MediaTypeHeaderValue> GetContentTypeAsync(this HttpClient httpClient, string url)
        {
            try
            {
                return await RetryPolicy.ExecuteAsync(async () =>
                {
                    return (await httpClient.SendAsync(new HttpRequestMessage
                    {
                        RequestUri = new Uri(url),
                        Method = HttpMethod.Head
                    }, HttpCompletionOption.ResponseHeadersRead)).Content?.Headers.ContentType;
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error retrieving filesize for Url: '{url}'");

                return null;
            }
        }
    }
}
