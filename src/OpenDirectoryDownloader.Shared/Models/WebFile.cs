using System.Diagnostics;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Shared.Models;

[DebuggerDisplay("{Url}, {FileSize} bytes")]
public class WebFile
{
	public string Url { get; set; }
	public string FileName { get; set; }
	public long? FileSize { get; set; }
	public string Description { get; set; }

	/// <summary>
	/// Directory this file was discovered under. Only set/used by the scan-database eviction path
	/// (issue #56 phase 4), to find which directory's PendingWork to decrement once this file's size
	/// lookup completes (or fails) - see OpenDirectoryIndexer.TryCloseDirectory.
	/// </summary>
	[JsonIgnore]
	public WebDirectory ParentDirectory { get; set; }
}
