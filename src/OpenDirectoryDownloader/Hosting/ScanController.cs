namespace OpenDirectoryDownloader.Hosting;

/// <summary>The running scan, as the console keys see it: pause it, wait for it, know whether it is done.</summary>
public sealed class ScanController
{
	private readonly CancellationTokenSource _pause = new();
	private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Cancelled by a pause: the crawl stops, the database keeps what it has for --resume</summary>
	public CancellationToken PauseToken => _pause.Token;

	public bool Paused { get; private set; }

	/// <summary>The crawl and the outputs are done</summary>
	public bool Finished { get; private set; }

	/// <summary>Completes when the scan (and its outputs) ended, however it ended</summary>
	public Task Completion => _completed.Task;

	/// <summary>Stops the crawl cleanly and waits until the database is saved.</summary>
	public async Task PauseAsync()
	{
		if (Finished || _pause.IsCancellationRequested)
		{
			await Completion;
			return;
		}

		Paused = true;
		await _pause.CancelAsync();
		await Completion;
	}

	internal void MarkFinished() => Finished = true;

	internal void MarkCompleted() => _completed.TrySetResult();
}
