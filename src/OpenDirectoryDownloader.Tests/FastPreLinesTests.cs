using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OpenDirectoryDownloader.Tests.Integration;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class FastPreLinesTests
{
	// The separator ParsePreDirectoryListing used on the serialized content before FastPreLines existed
	private static readonly Regex LineSeparator = new(@"\r\n|\r|\n|<br\S*>|<hr>");

	private static List<string> Reference(IElement pre) => [.. LineSeparator.Split(pre.FastInnerHtml())];

	private static IElement Pre(string html) => new HtmlParser().ParseDocument(html).QuerySelector("pre");

	[Theory]
	[InlineData("<pre>one\ntwo\nthree</pre>")]
	[InlineData("<pre>\nleading newline is dropped by the parser, trailing kept\n</pre>")]
	[InlineData("<pre>a\n\n\nb</pre>")]
	[InlineData("<pre><a href=\"x\">x</a> 1\n<a href=\"y\">y</a> 2\n</pre>")]
	[InlineData("<pre><hr><a href=\"x\">x</a><hr>text<hr></pre>")]
	[InlineData("<pre>before<hr>after<hr>\n<hr></pre>")]
	[InlineData("<pre><hr class=\"x\">not a separator</pre>")]
	[InlineData("<pre>a &amp; b &lt;dir&gt; &nbsp; c\nd</pre>")]
	[InlineData("<pre><img src=\"i.gif\" alt=\"[DIR]\"> <a href=\"d/\">d/</a>   02-Jan-2024 10:11    -   \n</pre>")]
	[InlineData("<pre><b>bold <i>nested</i></b> text\nnext <span class=\"c\">line</span></pre>")]
	[InlineData("<pre></pre>")]
	[InlineData("<pre>only text</pre>")]
	public void GivesTheSameLinesAsTheRegex(string html)
	{
		IElement pre = Pre(html);
		List<string> fast = pre.FastPreLines();

		Assert.NotNull(fast);
		Assert.Equal(Reference(pre), fast);
	}

	[Theory]
	[InlineData("<pre>line one<br>line two<br/>line three</pre>")]
	[InlineData("<pre><br><a href=\"x\">swallowed by the regex</a></pre>")]
	[InlineData("<pre>carriage&#13;return</pre>")]
	[InlineData("<pre>crlf&#13;\nbreak</pre>")]
	[InlineData("<pre><a href=\"x\" title=\"two&#10;lines\">x</a></pre>")]
	[InlineData("<pre><a href=\"x\"><hr></a></pre>")]
	public void DeclinesWhenTheRegexIsNeeded_AndTheFallbackStillWorks(string html)
	{
		IElement pre = Pre(html);

		Assert.Null(pre.FastPreLines());

		// the string-and-regex way is unchanged, so the caller's fallback gives a result
		Assert.NotEmpty(Reference(pre));
	}

	[Fact]
	public void OversizedElement_FallsBackInsteadOfBuildingHugeLines()
	{
		IElement pre = Pre($"<pre><a href=\"x\" title=\"{new string('t', 5000)}\">x</a></pre>");

		Assert.Null(pre.FastPreLines());
	}

	[Theory]
	[InlineData(ListingStyle.ApachePre)]
	[InlineData(ListingStyle.NginxAutoindex)]
	public void GeneratedListings_UseTheFastPath_AndMatch(ListingStyle style)
	{
		FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 1, SubdirectoriesPerDirectory = 3, FilesPerDirectory = 3_000 });
		IElement pre = Pre(site.RenderListing("/"));
		List<string> fast = pre.FastPreLines();

		Assert.NotNull(fast);
		Assert.Equal(Reference(pre), fast);
	}

	[Fact]
	public void EveryPreOfEverySampleListing_GivesTheSameLines()
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

				compared++;
				List<string> fast = pre.FastPreLines();

				if (fast is null)
				{
					continue;
				}

				fastPath++;
				List<string> expected = Reference(pre);

				Assert.True(expected.SequenceEqual(fast), $"Different lines for a <pre> in {path}: expected {expected.Count} lines, got {fast.Count}");
			}
		}

		Assert.True(compared > 50, $"Expected a real corpus, only compared {compared} <pre> elements");
		Assert.True(fastPath > compared / 2, $"The fast path should handle most <pre> elements ({fastPath} of {compared})");
	}
}
