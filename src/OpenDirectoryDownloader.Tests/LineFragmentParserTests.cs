using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OpenDirectoryDownloader.Tests.Integration;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class LineFragmentParserTests
{
	private static readonly Regex LineSeparator = new(@"\r\n|\r|\n|<br\S*>|<hr>");

	private static string Describe(IElement element) => element.InnerHtml;

	[Theory]
	[InlineData("<a href=\"file.mkv\">file.mkv</a>                  02-Jan-2024 10:11     1503238553")]
	[InlineData("<img src=\"/icons/unknown.gif\" alt=\"[   ]\"> <a href=\"file.mkv\">file.mkv</a>   2024-01-02 10:11  1.4G   ")]
	[InlineData("<a href=\"../\">../</a>")]
	[InlineData("<a href=\"sub/\">sub/</a>   02-Jan-2024 10:11   -")]
	[InlineData("<img src=\"/icons/back.gif\" alt=\"[PARENTDIR]\"> <a href=\"/\">Parent Directory</a>   -   ")]
	public void SimpleLines_AreHandledDirectly_AndMatchAngleSharp(string line)
	{
		IElement simple = LineFragmentParser.TryParseSimple(line);

		Assert.NotNull(simple);
		Assert.Equal(Describe(LineFragmentParser.ParseWithAngleSharp(line)), Describe(simple));
	}

	[Theory]
	[InlineData("<A HREF=\"file.mkv\">file.mkv</A>")]
	[InlineData("<a href='file.mkv'>file.mkv</a>")]
	[InlineData("<a href=file.mkv>file.mkv</a>")]
	[InlineData("<a href=\"a&amp;b\">a&amp;b</a>")]
	[InlineData("<a href=\"x\" href=\"y\">x</a>")]
	[InlineData("<a href=\"x\"title=\"t\">x</a>")]
	[InlineData("<a href=\"x\"><b>x</b></a>")]
	[InlineData("<a href=\"x\">unclosed")]
	[InlineData("<span>x</span>")]
	[InlineData("text with &lt;dir&gt;")]
	[InlineData("<a href=\"x\" disabled>x</a>")]
	public void AnythingElse_IsLeftToAngleSharp(string line)
	{
		Assert.Null(LineFragmentParser.TryParseSimple(line));

		// ...and the public entry point still produces a DOM for it
		Assert.NotNull(LineFragmentParser.Parse(line));
	}

	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	public void GeneratedListings_AreAlmostAlwaysSimple_AndIdenticalToAngleSharp(ListingStyle style)
	{
		FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 1, SubdirectoriesPerDirectory = 5, FilesPerDirectory = 2_000 });
		IElement pre = new HtmlParser().ParseDocument(site.RenderListing("/")).QuerySelector("pre");
		List<string> lines = LineSeparator.Split(pre.FastInnerHtml()).ToList();

		int simple = 0;

		foreach (string line in lines)
		{
			IElement fast = LineFragmentParser.TryParseSimple(line);

			if (fast is null)
			{
				continue;
			}

			simple++;
			Assert.Equal(Describe(LineFragmentParser.ParseWithAngleSharp(line)), Describe(fast));
		}

		Assert.True(simple > lines.Count * 0.99, $"Only {simple} of {lines.Count} lines took the fast path");
	}

	[Fact]
	public void EveryLineOfEverySampleListing_GivesTheSameDomOnBothPaths()
	{
		HtmlParser parser = new();
		int compared = 0;
		int fastPath = 0;

		foreach (string path in Directory.EnumerateFiles("Samples", "*.dat"))
		{
			foreach (IElement pre in parser.ParseDocument(File.ReadAllText(path)).QuerySelectorAll("pre"))
			{
				if (pre.ChildNodes.Length > 5_000)
				{
					continue;
				}

				foreach (string line in LineSeparator.Split(pre.FastInnerHtml()))
				{
					compared++;
					IElement fast = LineFragmentParser.TryParseSimple(line);

					if (fast is null)
					{
						continue;
					}

					fastPath++;
					string expected = Describe(LineFragmentParser.ParseWithAngleSharp(line));
					Assert.True(expected == Describe(fast), $"Different DOM in {path} for line: {line}");
				}
			}
		}

		Assert.True(compared > 1_000, $"Expected a real corpus, only compared {compared} lines");
		Assert.True(fastPath > 100, $"The fast path was never exercised on the samples ({fastPath} lines)");
	}

	[Fact]
	public async Task ManyThreads_ParseLinesWithoutInterfering()
	{
		string line = "<img src=\"/icons/unknown.gif\" alt=\"[   ]\"> <a href=\"file.mkv\">file.mkv</a>   2024-01-02 10:11  1.4G   ";
		string expected = Describe(LineFragmentParser.ParseWithAngleSharp(line));

		await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
		{
			for (int i = 0; i < 5_000; i++)
			{
				Assert.Equal(expected, Describe(LineFragmentParser.Parse(line)));
			}
		})));
	}
}
