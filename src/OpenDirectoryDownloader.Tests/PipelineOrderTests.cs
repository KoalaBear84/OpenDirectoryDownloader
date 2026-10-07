using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Outputs;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// The order of the checks is the behavior: a page that matches several kinds of listing is parsed by the first.
/// These pin the orders down, so changing one is a deliberate act (the golden parse tests show the effect).
/// </summary>
public class PipelineOrderTests
{
	[Fact]
	public void ListingPipeline_HasItsStepsInThisOrder()
	{
		Assert.Equal(
		[
			"BlitzfilesTech",
			"Dropbox",
			"GitHub",
			"GoFileIO",
			"Mediafire",
			"pCloud (host)",
			"Pixeldrain",
			"HFS (Server header)",
			"BuildDom",
			"IPFS",
			"Dufs",
			"File Browser",
			"AList",
			"Google Drive index detection",
			"Google Drive index",
			"SNT index generator",
			"Remove #sidebar and nav",
			"HFS (legacy title)",
			"pCloud (title)",
			"directory-listing.com",
			"h5ai",
			"Snif",
			"Godir",
			"Remove .breadcrumb",
			"Custom div listing",
			"Custom div listing 2",
			"HFS (div#files)",
			"copyparty",
			"Raw <pre> shortcut",
			"<pre>",
			"JavaScript drawn",
			"ul#root li",
			"dirLIST",
			"Tables",
			"Material design",
			"Directory Lister 01",
			"Directory Lister 02",
			".list-group li",
			"ul li",
			"All links",
			"Directory listing model 01",
			"Clean up",
			"Browser",
			"Iframe hint"
		], DefaultListingPipeline.Steps.Select(step => step.Name));
	}

	[Fact]
	public void OutputPipeline_HasItsStepsInThisOrder()
	{
		// The steps only keep what they are given, nothing runs here
		ScanOutputPipeline pipeline = new(
			new RedditStatsOutput(),
			new UrlListOutput(null, null, null),
			new UrlUploadOutput(null, null, null, null),
			new Aria2Output(null, null, null),
			new SpeedtestOutput(null, null, null, null),
			new StatisticsLogOutput(null, null, null),
			new SessionJsonOutput(null, null),
			new FinishedOutput(null, null),
			new ClipboardOutput(null, null),
			NullLogger<ScanOutputPipeline>.Instance);

		// The URL list before its upload, the statistics (with the upload's link) after both
		Assert.Equal(["Reddit statistics", "URL list", "URL list upload", "aria2 URL list", "Speedtest", "Statistics", "JSON", "Finished", "Clipboard"], pipeline.Steps.Select(step => step.Name));
	}
}
