using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Urls;

namespace OpenDirectoryDownloader.Outputs;

/// <summary>Where the output files go: Scans/&lt;the URL&gt;.&lt;extension&gt;, or --output-file.&lt;extension&gt;.</summary>
public sealed class OutputPaths(ScanSession session, OutputOptions outputOptions)
{
	public string ScansDirectory
	{
		get
		{
			string scansPath = Path.Combine(Directory.GetCurrentDirectory(), "Scans");
			Directory.CreateDirectory(scansPath);

			return scansPath;
		}
	}

	/// <summary>The full path of the output file with this extension (without the dot)</summary>
	public string Get(string extension)
	{
		string fileName = outputOptions.OutputFile is not null ? $"{outputOptions.OutputFile}.{extension}" : $"{UrlHelper.CleanUriToFilename(session.Root.Uri)}.{extension}";

		return Path.IsPathFullyQualified(fileName) ? fileName : Path.Combine(ScansDirectory, fileName);
	}
}
