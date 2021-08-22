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
                    Logger.Warn($"Error {ex.Message} retrieving on try {retryCount} for url '{context["Url"]}'. Waiting {span.TotalSeconds} seconds.");
                }
            );

        public static async Task<long?> GetUrlFileSizeAsync(this HttpClient httpClient, string url)
        {
            try
            {
                Context pollyContext = new Context
                {
                    { "Url", url }
                };

                return (await RetryPolicy.ExecuteAndCaptureAsync(ctx => GetUrlFileSizeInnerAsync(httpClient, url), pollyContext)).Result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error retrieving filesize for Url: '{url}'");

                return null;
            }
        }

        private static async Task<long?> GetUrlFileSizeInnerAsync(HttpClient httpClient, string url)
        {
            HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Head
            }, HttpCompletionOption.ResponseHeadersRead);

            try
            {
                if (
                    (new Uri(url)).Host == "the-eye.eu" && // workaround until we come up with a better way to fix this **without slowing down scans of ODs that don't report `Content-Length` at all for some files** (e.g. .php, .html)
                    httpResponseMessage.Content?.Headers.ContentLength == null
                ) {
                    throw new Exception("Missing Content-Length header!");
                }
                return httpResponseMessage.Content?.Headers.ContentLength;
            }
            finally
            {
                httpResponseMessage.Dispose();
            }
        }

        public static async Task<long?> GetUrlFileSizeByDownloadingAsync(this HttpClient httpClient, string url)
        {
            try
            {
                Context pollyContext = new Context
                {
                    { "Url", url }
                };

                return (await RetryPolicy.ExecuteAndCaptureAsync(ctx => GetUrlFileSizeByDownloadingInnerAsync(httpClient, url), pollyContext)).Result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error retrieving filesize for Url: '{url}'");

                return null;
            }
        }

        private static async Task<long?> GetUrlFileSizeByDownloadingInnerAsync(HttpClient httpClient, string url)
        {
            return (await httpClient.SendAsync(new HttpRequestMessage
            {
                RequestUri = new Uri(url)
            }, HttpCompletionOption.ResponseContentRead)).Content?.Headers.ContentLength;
        }

        public static async Task<MediaTypeHeaderValue> GetContentTypeAsync(this HttpClient httpClient, string url)
        {
            try
            {
                Context pollyContext = new Context
                {
                    { "Url", url }
                };

                return (await RetryPolicy.ExecuteAndCaptureAsync(ctx => GetContentTypeInnerAsync(httpClient, url), pollyContext)).Result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error retrieving filesize for Url: '{url}'");

                return null;
            }
        }

        private static async Task<MediaTypeHeaderValue> GetContentTypeInnerAsync(HttpClient httpClient, string url)
        {
            HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Head
            }, HttpCompletionOption.ResponseHeadersRead);

            try
            {
                return httpResponseMessage.Content?.Headers.ContentType;
            }
            finally
            {
                httpResponseMessage.Dispose();
            }
        }
    }
}
