using OpenDirectoryDownloader.Helpers;
using System.Diagnostics;

namespace OpenDirectoryDownloader.Outputs.FileUpload;

/// <summary>
/// Wraps the file that is being uploaded and writes the progress to the console as it is read by the HTTP client.
/// Length and Position are passed on, so the request keeps its Content-Length (no chunked upload).
/// </summary>
public sealed class UploadProgressStream(Stream inner, string siteName) : Stream
{
	private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(250);

	private readonly Stopwatch stopwatch = Stopwatch.StartNew();
	private readonly bool interactive = !Console.IsOutputRedirected;
	private TimeSpan lastReport = TimeSpan.FromSeconds(-1);
	private int lastPercentage = -1;
	private bool lineOpen;
	private bool completed;

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		int read = await inner.ReadAsync(buffer, cancellationToken);
		Report(read);

		return read;
	}

	public override int Read(Span<byte> buffer)
	{
		int read = inner.Read(buffer);
		Report(read);

		return read;
	}

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	private void Report(int read)
	{
		long length = inner.Length;
		long position = inner.Position;

		// The 100% is shown once: the last chunk reaches it, and the read after it (0 bytes) would show it again
		if (length <= 0 || completed)
		{
			return;
		}

		bool done = read == 0 || position >= length;
		int percentage = (int)(position * 100 / length);

		if (interactive)
		{
			if (!done && stopwatch.Elapsed - lastReport < ReportInterval)
			{
				return;
			}
		}
		else if (!done && (percentage / 10 == lastPercentage / 10))
		{
			// Redirected output: a line per 10% instead of a constantly rewritten line
			return;
		}

		lastReport = stopwatch.Elapsed;
		lastPercentage = percentage;

		double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
		string text = $"Uploading to {siteName}: {percentage,3}% ({FileSizeHelper.ToHumanReadable(position)} of {FileSizeHelper.ToHumanReadable(length)}, {FileSizeHelper.ToHumanReadable((long)(position / seconds))}/s)";

		if (interactive)
		{
			Console.Write($"\r{text}   ");
			lineOpen = true;

			if (done)
			{
				Console.WriteLine();
				lineOpen = false;
			}
		}
		else
		{
			Console.WriteLine(text);
		}

		completed = done;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			// Ended before the end (an error): don't leave the cursor in the middle of the progress line
			if (lineOpen && !completed)
			{
				Console.WriteLine();
			}

			inner.Dispose();
		}

		base.Dispose(disposing);
	}

	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => inner.CanSeek;
	public override bool CanWrite => false;
	public override long Length => inner.Length;
	public override long Position { get => inner.Position; set => inner.Position = value; }
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
