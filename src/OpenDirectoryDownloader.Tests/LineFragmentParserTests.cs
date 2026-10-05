using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OpenDirectoryDownloader.Tests.Integration;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class LineFragmentParserTests
{
	private static readonly Regex LineSeparator = new(@"\r\n|\r|\n|<br\S*>|<hr>");

	/// <summary>What the parsers saw before LineView existed: the answers read from AngleSharp's DOM.</summary>
	private static string FromAngleSharp(string line) => LineFragmentParser.ReadDom(LineFragmentParser.ParseWithAngleSharp(line)).ToString();

	[Theory]
	[InlineData("<a href=\"file.mkv\">file.mkv</a>                  02-Jan-2024 10:11     1503238553")]
	[InlineData("<img src=\"/icons/unknown.gif\" alt=\"[   ]\"> <a href=\"file.mkv\">file.mkv</a>   2024-01-02 10:11  1.4G   ")]
	[InlineData("<a href=\"../\">../</a>")]
	[InlineData("<a href=\"sub/\">sub/</a>   02-Jan-2024 10:11   -")]
	[InlineData("<img src=\"/icons/back.gif\" alt=\"[PARENTDIR]\"> <a href=\"/\">Parent Directory</a>   -   ")]
	[InlineData("<img src=\"/icons/folder.gif\" alt=\"[DIR]\"> <a href=\"x/\" title=\"hello\">x/</a>")]
	[InlineData("<a href=\"a\">first</a> <a href=\"b\" title=\"t\">second</a>")]
	[InlineData("<a>no href</a>")]
	[InlineData("<a href=\"x\"></a>")]
	[InlineData("<img alt=\"[ICO]\"><img alt=\"[DIR]\"><img alt=\"[   ]\"><img src=\"no-alt.gif\"><a href=\"y\">y</a>")]
	[InlineData("just text, no markup at all")]
	[InlineData("")]
	public void SimpleLines_AreHandledDirectly_AndAnswerLikeTheDom(string line)
	{
		LineView simple = LineFragmentParser.TryParseSimple(line);

		Assert.NotNull(simple);
		Assert.Equal(FromAngleSharp(line), simple.ToString());
	}

	[Theory]
	[InlineData("<A HREF=\"file.mkv\">file.mkv</A>")]
	[InlineData("<a href='file.mkv'>file.mkv</a>")]
	[InlineData("<a href=file.mkv>file.mkv</a>")]
	[InlineData("<a href=\"a&amp;b\">a&amp;b</a>")]
	[InlineData("<a href=\"x\" href=\"y\">x</a>")]
	[InlineData("<a href=\"x\" title=\"1\" title=\"2\">x</a>")]
	[InlineData("<img alt=\"a\" alt=\"b\">")]
	[InlineData("<a href=\"x\"title=\"t\">x</a>")]
	[InlineData("<a href=\"x\"><b>x</b></a>")]
	[InlineData("<a href=\"x\">unclosed")]
	[InlineData("<span>x</span>")]
	[InlineData("text with &lt;dir&gt;")]
	[InlineData("<a href=\"x\" disabled>x</a>")]
	public void AnythingElse_IsLeftToAngleSharp(string line)
	{
		Assert.Null(LineFragmentParser.TryParseSimple(line));

		// ...and the public entry point still answers, from the DOM
		Assert.Equal(FromAngleSharp(line), LineFragmentParser.Parse(line).ToString());
	}

	[Fact]
	public void HasImage_IsAnExactCaseSensitiveMatchOnAlt_LikeTheAttributeSelectorWas()
	{
		string line = "<img alt=\"[dir]\"> <img alt=\"[DIR] \"> <a href=\"x\">x</a>";
		IElement dom = LineFragmentParser.ParseWithAngleSharp(line);

		foreach (string alt in new[] { "[DIR]", "[dir]", "[DIR] ", "[ICO]" })
		{
			LineView simple = LineFragmentParser.TryParseSimple(line);
			bool expected = dom.QuerySelector($"img[alt=\"{alt}\"]") is not null;

			Assert.NotNull(simple);
			Assert.Equal(expected, simple.HasImage(alt));
			Assert.Equal(expected, LineFragmentParser.ReadDom(dom).HasImage(alt));
		}
	}

	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	public void GeneratedListings_AreAlmostAlwaysSimple_AndAnswerLikeTheDom(ListingStyle style)
	{
		FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 1, SubdirectoriesPerDirectory = 5, FilesPerDirectory = 2_000 });
		IElement pre = new HtmlParser().ParseDocument(site.RenderListing("/")).QuerySelector("pre");
		List<string> lines = LineSeparator.Split(pre.FastInnerHtml()).ToList();

		int simple = 0;

		foreach (string line in lines)
		{
			LineView fast = LineFragmentParser.TryParseSimple(line);

			if (fast is null)
			{
				continue;
			}

			simple++;
			Assert.Equal(FromAngleSharp(line), fast.ToString());
		}

		Assert.True(simple > lines.Count * 0.99, $"Only {simple} of {lines.Count} lines took the fast path");
	}

	[Fact]
	public void EveryLineOfEverySampleListing_GivesTheSameAnswersOnBothPaths()
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
					LineView fast = LineFragmentParser.TryParseSimple(line);

					if (fast is null)
					{
						continue;
					}

					fastPath++;
					string expected = FromAngleSharp(line);
					Assert.True(expected == fast.ToString(), $"Different answers in {path} for line: {line}{Environment.NewLine}expected {expected}{Environment.NewLine}actual   {fast}");
				}
			}
		}

		Assert.True(compared > 1_000, $"Expected a real corpus, only compared {compared} lines");
		Assert.True(fastPath > 100, $"The fast path was never exercised on the samples ({fastPath} lines)");
	}

	[Fact]
	public async Task ManyThreads_ParseLinesWithoutInterfering()
	{
		string simple = "<img src=\"/icons/unknown.gif\" alt=\"[   ]\"> <a href=\"file.mkv\">file.mkv</a>   2024-01-02 10:11  1.4G   ";
		string complex = "<a href=\"a&amp;b\">a&amp;b</a> <b>bold</b>";
		string expectedSimple = FromAngleSharp(simple);
		string expectedComplex = FromAngleSharp(complex);

		await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
		{
			for (int i = 0; i < 5_000; i++)
			{
				Assert.Equal(expectedSimple, LineFragmentParser.Parse(simple).ToString());
				Assert.Equal(expectedComplex, LineFragmentParser.Parse(complex).ToString());
			}
		})));
	}
}
