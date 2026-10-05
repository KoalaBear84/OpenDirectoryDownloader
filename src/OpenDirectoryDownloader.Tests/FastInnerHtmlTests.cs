using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class FastInnerHtmlTests
{
	private static string Context(string value, int index)
	{
		int start = Math.Max(0, index - 40);

		return value.Substring(start, Math.Min(120, value.Length - start)).Replace("\r", "\\r").Replace("\n", "\\n");
	}

	[Fact]
	public void SameAsInnerHtml_ForEveryElementInEverySampleListing()
	{
		HtmlParser parser = new();
		int compared = 0;

		foreach (string path in Directory.EnumerateFiles("Samples", "*.dat"))
		{
			string html = File.ReadAllText(path);

			foreach (IElement element in parser.ParseDocument(html).QuerySelectorAll("pre, td, li, div, a, body"))
			{
				// Only up to a modest size: the point is comparing output, and InnerHtml itself is the slow one
				if (element.ChildNodes.Length > 3_000)
				{
					continue;
				}

				string expected = element.InnerHtml;
				string actual = element.FastInnerHtml();

				if (expected != actual)
				{
					int index = 0;

					while (index < expected.Length && index < actual.Length && expected[index] == actual[index])
					{
						index++;
					}

					Assert.Fail($"Different output for <{element.LocalName}> in {path} at {index}: expected '{Context(expected, index)}' but got '{Context(actual, index)}'");
				}

				compared++;
			}
		}

		Assert.True(compared > 1_000, $"Expected a real corpus, compared only {compared} elements");
	}

	[Theory]
	[InlineData("<pre>a &amp; b &lt;dir&gt; <a href=\"x?a=1&amp;b=2\" title='say \"hi\"'>x&nbsp;y</a><img src=\"i.gif\" alt=\"[DIR]\"><br><hr>end</pre>")]
	[InlineData("<pre><!-- comment --><a href=\"/\">root</a><script>var a = '<b>';</script><pre>nested</pre></pre>")]
	[InlineData("<pre><svg><a xlink:href=\"u\">t</a></svg><textarea>\nx</textarea><b>bold<i>it</i></b></pre>")]
	public void SameAsInnerHtml_ForTrickyMarkup(string html)
	{
		IElement pre = new HtmlParser().ParseDocument(html).QuerySelector("pre");

		Assert.Equal(pre.InnerHtml, pre.FastInnerHtml());
	}
}
