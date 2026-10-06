using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>Finds a file's size with a HEAD request, for listings that don't show sizes (or with --exact-file-sizes).</summary>
public sealed class FileSizeLookup(ScanHttp scanHttp, ILogger<FileSizeLookup> logger)
{
	private readonly ResiliencePipeline _retry = new ResiliencePipelineBuilder()
		.AddRetry(new RetryStrategyOptions
		{
			ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
			MaxRetryAttempts = 4,
			DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1))),
			OnRetry = args =>
			{
				logger.LogWarning("Error {error} retrieving on try {retryCount} for url '{url}'. Waiting {waitTime:F0} seconds.", args.Outcome.Exception?.Message, args.AttemptNumber + 1, args.Context.Properties.GetValue(UrlKey, null), args.RetryDelay.TotalSeconds);

				return default;
			}
		})
		.Build();

	private static readonly ResiliencePropertyKey<string> UrlKey = new("Url");

	/// <returns>The Content-Length, or null when the server does not tell (or keeps failing)</returns>
	public async Task<long?> GetFileSizeAsync(string url, CancellationToken cancellationToken)
	{
		ResilienceContext context = ResilienceContextPool.Shared.Get(cancellationToken);
		context.Properties.Set(UrlKey, url);

		try
		{
			return await _retry.ExecuteAsync(static async (context, state) => await state.Lookup.HeadAsync(state.Url, context.CancellationToken), context, (Lookup: this, Url: url));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Error retrieving filesize for Url: '{url}'", url);

			return null;
		}
		finally
		{
			ResilienceContextPool.Shared.Return(context);
		}
	}

	private async ValueTask<long?> HeadAsync(string url, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new(HttpMethod.Head, url);
		using HttpResponseMessage httpResponseMessage = await scanHttp.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

		// Workaround until there is a better way, without slowing down scans of ODs that don't report Content-Length at all for some files (.php, .html)
		if (new Uri(url).Host == "the-eye.eu" && httpResponseMessage.Content?.Headers.ContentLength == null)
		{
			throw new Exception("Missing Content-Length header!");
		}

		return httpResponseMessage.Content?.Headers.ContentLength;
	}
}
