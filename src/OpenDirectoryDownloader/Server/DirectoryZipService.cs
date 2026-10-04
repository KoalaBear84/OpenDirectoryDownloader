using OpenDirectoryDownloader.Storage;
using System.IO.Compression;

namespace OpenDirectoryDownloader.Server;

/// <summary>
/// Live progress for one in-flight "download directory as ZIP" request, shared between the endpoint that
/// streams the ZIP bytes and the endpoint that streams progress updates for it (see 'Web viewer' in the
/// README). FilesDone/BytesWritten/Skipped are only ever incremented by the single ZIP-building request
/// they belong to; the progress endpoint only reads them, so plain fields are enough - no locking needed.
/// </summary>
public sealed class ZipJobProgress
{
	public long FilesDone;
	public long TotalFiles;
	public long BytesWritten;
	public long Skipped;
	public volatile string CurrentFile;
	public volatile bool IsComplete;
	public volatile string ErrorMessage;
}

/// <summary>Builds a ZIP of a scanned directory's subtree on the fly, fetching each file's bytes from the original site as it goes.</summary>
public static class DirectoryZipService
{
	/// <summary>
	/// Writes a ZIP of <paramref name="directoryUrl"/> (using <paramref name="directoryName"/> as the
	/// top-level folder inside the archive) to <paramref name="destination"/>, fetching every file through
	/// <paramref name="httpClient"/>. A single file that fails to download is skipped (counted in
	/// <see cref="ZipJobProgress.Skipped"/>) rather than aborting the whole ZIP.
	/// </summary>
	public static async Task WriteZipAsync(ScanDatabase.SubtreeResult subtree, string directoryUrl, string directoryName, HttpClient httpClient, Stream destination, ZipJobProgress progress, CancellationToken cancellationToken)
	{
		Dictionary<string, ScanDatabase.MirroredDirectory> directoriesByUrl = subtree.Directories.ToDictionary(directory => directory.Url);

		progress.TotalFiles = subtree.Files.Count;

		using ZipArchive archive = new(destination, ZipArchiveMode.Create, leaveOpen: true);

		foreach (ScanDatabase.SubtreeFile file in subtree.Files)
		{
			cancellationToken.ThrowIfCancellationRequested();

			string relativePath = BuildRelativePath(file, directoryUrl, directoryName, directoriesByUrl);
			progress.CurrentFile = relativePath;

			try
			{
				using HttpResponseMessage response = await httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				response.EnsureSuccessStatusCode();

				ZipArchiveEntry entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);

				await using Stream entryStream = entry.Open();
				await using Stream remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);

				byte[] buffer = new byte[81920];
				int bytesRead;

				while ((bytesRead = await remoteStream.ReadAsync(buffer, cancellationToken)) > 0)
				{
					await entryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
					Interlocked.Add(ref progress.BytesWritten, bytesRead);
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Interlocked.Increment(ref progress.Skipped);
				Program.Logger.Warning(ex, "Skipping '{url}' while building a ZIP of '{directoryUrl}'", file.Url, directoryUrl);
			}

			Interlocked.Increment(ref progress.FilesDone);
		}
	}

	/// <summary>
	/// Reconstructs a file's path inside the ZIP from its DirectoryUrl's ParentUrl chain back up to
	/// <paramref name="rootUrl"/>, prefixed with <paramref name="rootName"/> so extracting the archive
	/// doesn't dump loose files into whatever folder it's extracted into. Every path segment is sanitized:
	/// directory/file names come from the remote site's own (untrusted) listing, so a crafted "../" name
	/// must not be able to place a ZIP entry outside the archive's own folder (zip-slip).
	/// </summary>
	private static string BuildRelativePath(ScanDatabase.SubtreeFile file, string rootUrl, string rootName, Dictionary<string, ScanDatabase.MirroredDirectory> directoriesByUrl)
	{
		List<string> segments = [file.FileName];
		string currentUrl = file.DirectoryUrl;

		while (currentUrl is not null && currentUrl != rootUrl && directoriesByUrl.TryGetValue(currentUrl, out ScanDatabase.MirroredDirectory directory))
		{
			segments.Insert(0, directory.Name);
			currentUrl = directory.ParentUrl;
		}

		segments.Insert(0, rootName);

		return string.Join('/', segments.Select(SanitizeZipSegment));
	}

	private static string SanitizeZipSegment(string segment)
	{
		if (string.IsNullOrEmpty(segment) || segment is "." or "..")
		{
			return "_";
		}

		return segment.Replace('/', '_').Replace('\\', '_');
	}
}
