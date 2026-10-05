using OpenDirectoryDownloader.Shared.Models;
using System.Diagnostics;
using Xunit;

namespace OpenDirectoryDownloader.Tests.Integration;

/// <summary>
/// Guards against parsing a big listing getting slower than linear. Serializing a &lt;pre&gt; through AngleSharp's
/// InnerHtml took time quadratic in the number of links (80,000 files: 38 s), which made one big directory
/// minutes of work. The limits leave a wide margin over the normal ~3 s but sit far below the old behaviour (~25 s).
/// </summary>
public class LargeListingParsingTests
{
	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	public async Task ParseHtml_ListingWithManyFiles_StaysLinear(ListingStyle style)
	{
		Program.Logger ??= new Serilog.LoggerConfiguration().CreateLogger();

		const int files = 60_000;
		FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 0, FilesPerDirectory = files });
		string html = site.RenderListing("/");
		WebDirectory directory = new(null) { Url = "http://localhost:8086/", Name = "root" };

		Stopwatch stopwatch = Stopwatch.StartNew();
		WebDirectory parsed = await DirectoryParser.ParseHtml(directory, html);
		stopwatch.Stop();

		Assert.Equal(files, parsed.Files.Count);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(12), $"Parsing {files:N0} files took {stopwatch.Elapsed.TotalSeconds:F1} s, expected a few seconds");
	}
}
