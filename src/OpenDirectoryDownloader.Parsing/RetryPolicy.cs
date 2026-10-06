using Polly;
using Polly.Retry;

namespace OpenDirectoryDownloader.Parsing;

/// <summary>
/// Retries a site's API calls a few times with exponential backoff (2, 4, 8, 16 seconds), on Polly 8. Built once
/// per parser call, so the callback can name the directory it is for.
/// </summary>
public sealed class RetryPolicy
{
	private readonly ResiliencePipeline _pipeline;

	private RetryPolicy(ResiliencePipeline pipeline)
	{
		_pipeline = pipeline;
	}

	/// <param name="onRetry">Called with the error, the wait before the next attempt and the attempt number (1 based)</param>
	public static RetryPolicy Create(Action<Exception, TimeSpan, int> onRetry, int maxRetries = 4)
	{
		ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
			.AddRetry(new RetryStrategyOptions
			{
				ShouldHandle = new PredicateBuilder().Handle<Exception>(),
				MaxRetryAttempts = maxRetries,
				DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, args.AttemptNumber + 1)))),
				OnRetry = args =>
				{
					onRetry(args.Outcome.Exception, args.RetryDelay, args.AttemptNumber + 1);

					return default;
				}
			})
			.Build();

		return new RetryPolicy(pipeline);
	}

	public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
	{
		await _pipeline.ExecuteAsync(async _ => await action(), cancellationToken);
	}

	public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
	{
		return await _pipeline.ExecuteAsync(async _ => await action(), cancellationToken);
	}

	/// <summary>Like <see cref="ExecuteAsync(Func{Task}, CancellationToken)"/>, but returns the final error instead of throwing it.</summary>
	public async Task<Exception> ExecuteAndCaptureAsync(Func<Task> action, CancellationToken cancellationToken = default)
	{
		try
		{
			await ExecuteAsync(action, cancellationToken);

			return null;
		}
		catch (Exception ex)
		{
			return ex;
		}
	}
}
