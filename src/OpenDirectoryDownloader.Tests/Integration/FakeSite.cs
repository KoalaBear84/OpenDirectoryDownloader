using System.Globalization;
using System.Text;

namespace OpenDirectoryDownloader.Tests.Integration;

public enum ListingStyle
{
	/// <summary>Apache mod_autoindex: &lt;pre&gt; with icons, sizes like 1.4G (rounded).</summary>
	ApachePre,

	/// <summary>nginx autoindex: &lt;pre&gt; with exact byte sizes and dd-MMM-yyyy dates.</summary>
	NginxAutoindex,

	/// <summary>lighttpd mod_dirlisting: a &lt;table&gt; with Name / Last Modified / Size / Type columns.</summary>
	HtmlTable
}

/// <summary>
/// Describes a fake directory tree. Nothing is stored: every listing is generated from the path on demand,
/// so a directory with 500,000 files costs the test no memory beyond the single response being written.
/// </summary>
public sealed class FakeSiteOptions
{
	public ListingStyle Style { get; init; } = ListingStyle.ApachePre;

	/// <summary>Levels of subdirectories below the root (0 = only the root directory).</summary>
	public int Depth { get; init; } = 2;

	public int SubdirectoriesPerDirectory { get; init; } = 3;

	public int FilesPerDirectory { get; init; } = 5;

	/// <summary>Overrides the file count of specific directories, e.g. one huge directory. Receives the directory path ("/dir-01/").</summary>
	public Func<string, int?> FilesPerDirectoryOverride { get; init; }

	/// <summary>Return true to make a directory listing answer HTTP 500, to test error handling.</summary>
	public Func<string, bool> FailDirectory { get; init; }

	/// <summary>Artificial delay per request, to make slow servers.</summary>
	public TimeSpan Latency { get; init; } = TimeSpan.Zero;

	public string HostName { get; init; } = "files.example.test";

	/// <summary>Keep each rendered listing around instead of generating it again for every request. Use it when measuring the client (allocation, memory): otherwise the server's own work for a huge listing, in the same process, is counted too.</summary>
	public bool CacheListings { get; init; }
}

public sealed record FakeSiteExpectation(int Directories, long Files, long TotalBytes);

/// <summary>Deterministic directory tree plus the HTML listings for it.</summary>
public sealed class FakeSite(FakeSiteOptions options)
{
	private static readonly string[] Extensions = ["mkv", "mp4", "iso", "txt", "jpg"];
	private static readonly DateTime BaseTime = new(2024, 1, 2, 10, 11, 0, DateTimeKind.Utc);

	public FakeSiteOptions Options { get; } = options;

	public static string DirectoryName(int index) => $"dir-{index:D2}";

	public static string FileName(int index) => $"file-{index:D5}.{Extensions[index % Extensions.Length]}";

	/// <summary>Multiples of 1 KiB between 1 KiB and ~4 GiB, derived from the path so every run sees the same sizes.</summary>
	public static long FileSize(string directoryPath, int index)
	{
		unchecked
		{
			uint hash = 2166136261;

			foreach (char c in directoryPath)
			{
				hash = (hash ^ c) * 16777619;
			}

			hash = (hash ^ (uint)index) * 16777619;

			return 1024L * (1 + hash % 4_000_000);
		}
	}

	public bool DirectoryExists(string path)
	{
		string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (segments.Length > Options.Depth)
		{
			return false;
		}

		foreach (string segment in segments)
		{
			if (!segment.StartsWith("dir-", StringComparison.Ordinal) ||
			    !int.TryParse(segment.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
			    number < 1 || number > Options.SubdirectoriesPerDirectory)
			{
				return false;
			}
		}

		return true;
	}

	public int FileCount(string path) => Options.FilesPerDirectoryOverride?.Invoke(path) ?? Options.FilesPerDirectory;

	public IEnumerable<string> SubdirectoryPaths(string path)
	{
		if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= Options.Depth)
		{
			yield break;
		}

		for (int i = 1; i <= Options.SubdirectoriesPerDirectory; i++)
		{
			yield return $"{path}{DirectoryName(i)}/";
		}
	}

	/// <summary>What a correct scan must find, calculated by walking the same spec the server uses (no HTTP involved).</summary>
	public FakeSiteExpectation Expectation()
	{
		int directories = 0;
		long files = 0;
		long bytes = 0;

		void Walk(string path)
		{
			directories++;
			int fileCount = FileCount(path);
			files += fileCount;

			for (int i = 0; i < fileCount; i++)
			{
				bytes += FileSize(path, i);
			}

			foreach (string subdirectory in SubdirectoryPaths(path))
			{
				Walk(subdirectory);
			}
		}

		Walk("/");

		return new FakeSiteExpectation(directories, files, bytes);
	}

	public string RenderListing(string path)
	{
		StringBuilder html = new(1024 + FileCount(path) * 160);

		switch (Options.Style)
		{
			case ListingStyle.ApachePre:
				RenderApache(html, path);
				break;
			case ListingStyle.NginxAutoindex:
				RenderNginx(html, path);
				break;
			case ListingStyle.HtmlTable:
				RenderTable(html, path);
				break;
		}

		return html.ToString();
	}

	private string ParentLink(string path) => path == "/" ? "/" : path[..(path.TrimEnd('/').LastIndexOf('/') + 1)];

	private static string Modified(int index) => BaseTime.AddMinutes(index).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

	private static string ApacheSize(long bytes)
	{
		if (bytes < 1024)
		{
			return bytes.ToString(CultureInfo.InvariantCulture);
		}

		string[] units = ["K", "M", "G", "T"];
		double value = bytes;
		int unit = -1;

		while (value >= 1024 && unit < units.Length - 1)
		{
			value /= 1024;
			unit++;
		}

		return value < 10 ? $"{value.ToString("0.0", CultureInfo.InvariantCulture)}{units[unit]}" : $"{value.ToString("0", CultureInfo.InvariantCulture)}{units[unit]}";
	}

	private void RenderApache(StringBuilder html, string path)
	{
		html.Append("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 3.2 Final//EN\">\n<html>\n <head>\n  <title>Index of ").Append(path.TrimEnd('/')).Append("</title>\n </head>\n <body>\n<h1>Index of ").Append(path.TrimEnd('/')).Append("</h1>\n");
		html.Append("<pre><img src=\"/icons/blank.gif\" alt=\"Icon \"> <a href=\"?C=N;O=D\">Name</a>                    <a href=\"?C=M;O=A\">Last modified</a>      <a href=\"?C=S;O=A\">Size</a>  <a href=\"?C=D;O=A\">Description</a><hr>");
		html.Append("<img src=\"/icons/back.gif\" alt=\"[PARENTDIR]\"> <a href=\"").Append(ParentLink(path)).Append("\">Parent Directory</a>                             -   \n");

		int row = 0;

		foreach (string subdirectory in SubdirectoryPaths(path))
		{
			string name = subdirectory.TrimEnd('/')[(subdirectory.TrimEnd('/').LastIndexOf('/') + 1)..];
			html.Append("<img src=\"/icons/folder.gif\" alt=\"[DIR]\"> <a href=\"").Append(name).Append("/\">").Append(name).Append("/</a>").Append(' ', Math.Max(1, 24 - name.Length)).Append(Modified(row++)).Append("    -   \n");
		}

		int fileCount = FileCount(path);

		for (int i = 0; i < fileCount; i++)
		{
			string name = FileName(i);
			string size = ApacheSize(FileSize(path, i));
			html.Append("<img src=\"/icons/unknown.gif\" alt=\"[   ]\"> <a href=\"").Append(name).Append("\">").Append(name).Append("</a>").Append(' ', Math.Max(1, 25 - name.Length)).Append(Modified(row++)).Append("  ").Append(size.PadLeft(4)).Append("   \n");
		}

		html.Append("<hr></pre>\n<address>Apache/2.4.41 (Ubuntu) Server at ").Append(Options.HostName).Append(" Port 80</address>\n</body></html>\n");
	}

	private void RenderNginx(StringBuilder html, string path)
	{
		html.Append("<html>\r\n<head><title>Index of ").Append(path).Append("</title></head>\r\n<body>\r\n<h1>Index of ").Append(path).Append("</h1><hr><pre><a href=\"../\">../</a>\r\n");

		int row = 0;

		foreach (string subdirectory in SubdirectoryPaths(path))
		{
			string name = subdirectory.TrimEnd('/')[(subdirectory.TrimEnd('/').LastIndexOf('/') + 1)..];
			html.Append("<a href=\"").Append(name).Append("/\">").Append(name).Append("/</a>").Append(' ', Math.Max(1, 51 - name.Length - 1)).Append(NginxDate(row++)).Append("                   -\r\n");
		}

		int fileCount = FileCount(path);

		for (int i = 0; i < fileCount; i++)
		{
			string name = FileName(i);
			html.Append("<a href=\"").Append(name).Append("\">").Append(name).Append("</a>").Append(' ', Math.Max(1, 51 - name.Length)).Append(NginxDate(row++)).Append(FileSize(path, i).ToString(CultureInfo.InvariantCulture).PadLeft(20)).Append("\r\n");
		}

		html.Append("</pre><hr></body>\r\n</html>\r\n");
	}

	private static string NginxDate(int index) => BaseTime.AddMinutes(index).ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);

	private void RenderTable(StringBuilder html, string path)
	{
		html.Append("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>\n<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"en\">\n<head>\n<title>Index of ").Append(path).Append("</title>\n</head>\n<body>\n<h2>Index of ").Append(path).Append("</h2>\n<div class=\"list\">\n");
		html.Append("<table summary=\"Directory Listing\" cellpadding=\"0\" cellspacing=\"0\">\n<thead><tr><th class=\"n\">Name</th><th class=\"m\">Last Modified</th><th class=\"s\">Size</th><th class=\"t\">Type</th></tr></thead>\n<tbody>\n");
		html.Append("<tr><td class=\"n\"><a href=\"").Append(ParentLink(path)).Append("\">Parent Directory</a>/</td><td class=\"m\">&nbsp;</td><td class=\"s\">- &nbsp;</td><td class=\"t\">Directory</td></tr>\n");

		int row = 0;

		foreach (string subdirectory in SubdirectoryPaths(path))
		{
			string name = subdirectory.TrimEnd('/')[(subdirectory.TrimEnd('/').LastIndexOf('/') + 1)..];
			html.Append("<tr><td class=\"n\"><a href=\"").Append(name).Append("/\">").Append(name).Append("</a>/</td><td class=\"m\">").Append(BaseTime.AddMinutes(row++).ToString("yyyy-MMM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("</td><td class=\"s\">- &nbsp;</td><td class=\"t\">Directory</td></tr>\n");
		}

		int fileCount = FileCount(path);

		for (int i = 0; i < fileCount; i++)
		{
			string name = FileName(i);
			html.Append("<tr><td class=\"n\"><a href=\"").Append(name).Append("\">").Append(name).Append("</a></td><td class=\"m\">").Append(BaseTime.AddMinutes(row++).ToString("yyyy-MMM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("</td><td class=\"s\">").Append(FileSize(path, i).ToString("N0", CultureInfo.InvariantCulture).Replace(",", string.Empty)).Append("&nbsp;</td><td class=\"t\">application/octet-stream</td></tr>\n");
		}

		html.Append("</tbody>\n</table>\n</div>\n<div class=\"foot\">lighttpd/1.4.59</div>\n</body>\n</html>\n");
	}
}
