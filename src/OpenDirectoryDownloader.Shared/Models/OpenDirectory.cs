using System.Diagnostics;

namespace OpenDirectoryDownloader.Shared.Models
{
	[DebuggerDisplay("{Url,nq}, {Root}")]
	public class OpenDirectory
	{
		public string Url { get; set; }
		public bool Finished { get; set; }
		public WebDirectory Root { get; set; }
	}
}
