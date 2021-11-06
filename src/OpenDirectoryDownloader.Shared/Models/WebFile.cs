using System.Diagnostics;

namespace OpenDirectoryDownloader.Shared.Models;

[DebuggerDisplay("{Url}, {FileSize} bytes")]
public class WebFile
{
	public string Url { get; set; }
	public string FileName { get; set; }
	public long FileSize { get; set; }
	public string Description { get; set; }
}
