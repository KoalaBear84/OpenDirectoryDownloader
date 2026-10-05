using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OpenDirectoryDownloader.Tests.Integration;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// LineRegexFastPath reads regular Apache and nginx rows without running their regexes. It may only claim a line
/// when the regex would match it with the same group values: every claim is compared with the regex here, on real
/// listing lines, generated ones, mutated ones and lines built from the regex's own grammar with random variations.
/// </summary>
public class LineRegexFastPathTests
{
	private static readonly Regex LineSeparator = new(@"\r\n|\r|\n|<br\S*>|<hr>");

	private static readonly Regex NginxRegex = (Regex)typeof(DirectoryParser).GetMethod("RegexRegexParser2", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
	private static readonly Regex ApacheRegex = (Regex)typeof(DirectoryParser).GetMethod("RegexRegexParser1", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

	private static void AssertNginxClaimIsRight(string line)
	{
		if (!LineRegexFastPath.TryMatchNginx(line, out string fileSize))
		{
			return;
		}

		Match match = NginxRegex.Match(line);

		Assert.True(match.Success, $"nginx fast path claimed a line the regex does not match: {line}");
		Assert.True(match.Groups["FileSize"].Value.Trim() == fileSize, $"nginx fast path read '{fileSize}' but the regex says '{match.Groups["FileSize"].Value}' for: {line}");
	}

	private static void AssertApacheClaimIsRight(string line)
	{
		if (!LineRegexFastPath.TryMatchApache(line, out string fileSize, out string description))
		{
			return;
		}

		Match match = ApacheRegex.Match(line);

		Assert.True(match.Success, $"Apache fast path claimed a line the regex does not match: {line}");
		Assert.True(match.Groups["FileSize"].Value == fileSize, $"Apache fast path read size '{fileSize}' but the regex says '{match.Groups["FileSize"].Value}' for: {line}");
		Assert.True(match.Groups["Description"].Value == description, $"Apache fast path read description '{description}' but the regex says '{match.Groups["Description"].Value}' for: {line}");
	}

	private static List<string> ListingLines(ListingStyle style, int files)
	{
		FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 1, SubdirectoriesPerDirectory = 3, FilesPerDirectory = files });
		IElement pre = new HtmlParser().ParseDocument(site.RenderListing("/dir-01/")).QuerySelector("pre")!;

		return [.. LineSeparator.Split(pre.FastInnerHtml())];
	}

	private static List<string> SampleLines()
	{
		HtmlParser parser = new();
		List<string> lines = [];

		foreach (string path in Directory.EnumerateFiles("Samples", "*.dat"))
		{
			foreach (IElement pre in parser.ParseDocument(File.ReadAllText(path)).QuerySelectorAll("pre"))
			{
				if (pre.ChildNodes.Length <= 5_000)
				{
					lines.AddRange(LineSeparator.Split(pre.FastInnerHtml()));
				}
			}
		}

		return lines;
	}

	private static string Mutate(Random random, string line)
	{
		const string alphabet = "0123456789abcXYZ -:/<>\"=.,_;()[]& \t";
		char[] chars = line.ToCharArray();

		for (int edits = random.Next(1, 4); edits > 0 && chars.Length > 0; edits--)
		{
			int at = random.Next(chars.Length);

			switch (random.Next(3))
			{
				case 0:
					chars[at] = alphabet[random.Next(alphabet.Length)];
					break;
				case 1:
					chars = [.. chars[..at], .. chars[(at + 1)..]];
					break;
				default:
					chars = [.. chars[..at], alphabet[random.Next(alphabet.Length)], .. chars[at..]];
					break;
			}
		}

		return new string(chars);
	}

	private static string Pick(Random random, params string[] options) => options[random.Next(options.Length)];

	private static string Spaces(Random random, int max) => new(' ', random.Next(0, max + 1));

	private static string Digits(Random random, int maxLength) => new(Enumerable.Range(0, random.Next(1, maxLength + 1)).Select(_ => (char)('0' + random.Next(10))).ToArray());

	/// <summary>A line built from the nginx regex's own grammar, with random spacing, date shapes and trailing junk.</summary>
	private static string NginxGrammarLine(Random random)
	{
		StringBuilder line = new();
		line.Append(Pick(random, "<a href=\"", "<a  href=\"", "<a")).Append(Pick(random, "file.mkv", "a b", "d/", "x>y"));
		line.Append(Pick(random, "\">", "\" >", ">")).Append(Pick(random, "file.mkv", "d/", "a</b>b", "x")).Append("</a>");
		line.Append(Spaces(random, 6));
		line.Append(Pick(random, Digits(random, 2) + "-" + Pick(random, "Jan", "Feb", "01", "J_1", "Ja-n", "") + "-" + Digits(random, 4), "-", "", Digits(random, 2)));
		line.Append(Pick(random, " ", "  ", "", "\t", " "));
		line.Append(Digits(random, 2)).Append(Pick(random, ":", ":", ";", "")).Append(Pick(random, Digits(random, 1), Digits(random, 2), Digits(random, 3), ""));
		line.Append(Spaces(random, 5));
		line.Append(Pick(random, Digits(random, 12), "-", "1.4G", "230K", "12M", "", "1 2", "12 MB", "a-b", "é"));
		line.Append(Pick(random, "", " ", "   ", " extra", " extra words", "<", ">"));

		return line.ToString();
	}

	/// <summary>A line built from the Apache regex's own grammar, with random spacing, date shapes and descriptions.</summary>
	private static string ApacheGrammarLine(Random random)
	{
		StringBuilder line = new();
		line.Append(Pick(random, "<img src=\"/icons/unknown.gif\" alt=\"[   ]\">", "<img src=\"/i.gif\" alt=\"[DIR]\">", "<img src=\"x\">", "<img>", "<imgx>", "<img alt=\"a>b\">"));
		line.Append(Spaces(random, 3));
		line.Append(Pick(random, "<a href=\"file.mkv\">", "<a href=\"d/\">", "<a>", "<a href=\"x>y\">"));
		line.Append(Pick(random, "file.mkv", "d/", "a b", "x>y", ""));
		line.Append("</a>").Append(Pick(random, "", "", "", "x"));
		line.Append(Spaces(random, 6));
		line.Append(Pick(random,
			Digits(random, 4) + "-" + Digits(random, 2) + "-" + Digits(random, 2),
			Digits(random, 2) + "-" + Pick(random, "Jan", "Feb", "Mar") + "-" + Digits(random, 4),
			Digits(random, 2) + "-" + Digits(random, 2) + "-" + Digits(random, 2),
			Digits(random, 2) + "-" + Pick(random, "Jan1", "") + "-" + Digits(random, 2),
			"-", ""));
		line.Append(Pick(random, " ", "  ", "", "\t"));
		line.Append(Digits(random, 2)).Append(Pick(random, ":", ":", "")).Append(Digits(random, 2)).Append(Pick(random, "", "", ":" + Digits(random, 2), "Z"));
		line.Append(Spaces(random, 5));
		line.Append(Pick(random, "1.4G", "230K", "12M", "-", "", Digits(random, 8), "a b"));
		line.Append(Pick(random, "", "   ", " some description", "  ", "x", " "));

		return line.ToString();
	}

	[Fact]
	public void Nginx_EveryClaimMatchesTheRegex()
	{
		Random random = new(2024);
		List<string> real = [.. SampleLines().Concat(ListingLines(ListingStyle.NginxAutoindex, 400)).Distinct()];
		int claimed = 0;

		foreach (string line in real)
		{
			AssertNginxClaimIsRight(line);

			for (int variant = 0; variant < 20; variant++)
			{
				AssertNginxClaimIsRight(Mutate(random, line));
			}
		}

		for (int i = 0; i < 400_000; i++)
		{
			string line = NginxGrammarLine(random);
			AssertNginxClaimIsRight(line);
			claimed += LineRegexFastPath.TryMatchNginx(line, out _) ? 1 : 0;
		}

		// the claims are exercised for real, not vacuously
		Assert.True(claimed > 500, $"Only {claimed} of the generated nginx lines were claimed");
	}

	[Fact]
	public void Apache_EveryClaimMatchesTheRegex()
	{
		Random random = new(7);
		List<string> real = [.. SampleLines().Concat(ListingLines(ListingStyle.ApachePre, 400)).Distinct()];
		int claimed = 0;

		foreach (string line in real)
		{
			AssertApacheClaimIsRight(line);

			for (int variant = 0; variant < 20; variant++)
			{
				AssertApacheClaimIsRight(Mutate(random, line));
			}
		}

		for (int i = 0; i < 400_000; i++)
		{
			string line = ApacheGrammarLine(random);
			AssertApacheClaimIsRight(line);
			claimed += LineRegexFastPath.TryMatchApache(line, out _, out _) ? 1 : 0;
		}

		Assert.True(claimed > 5_000, $"Only {claimed} of the generated Apache lines were claimed");
	}

	[Theory]
	[InlineData(ListingStyle.NginxAutoindex)]
	[InlineData(ListingStyle.ApachePre)]
	public void GeneratedListings_AreClaimedForAlmostEveryRow(ListingStyle style)
	{
		List<string> lines = ListingLines(style, 2_000);
		Regex regex = style == ListingStyle.NginxAutoindex ? NginxRegex : ApacheRegex;
		int rows = 0;
		int claimed = 0;

		foreach (string line in lines.Where(l => regex.IsMatch(l) && l.Contains("</a>") && !l.Contains("Parent Directory")))
		{
			rows++;
			claimed += style == ListingStyle.NginxAutoindex ? (LineRegexFastPath.TryMatchNginx(line, out _) ? 1 : 0) : (LineRegexFastPath.TryMatchApache(line, out _, out _) ? 1 : 0);
		}

		Assert.True(rows > 1_500, $"Only {rows} rows");
		Assert.True(claimed >= rows * 0.99, $"Only {claimed} of {rows} rows were read without the regex");
	}

	[Theory]
	[InlineData("<a href=\"file.mkv\">file.mkv</a>                  02-Jan-2024 10:11     1503238553", "1503238553")]
	[InlineData("<a href=\"sub/\">sub/</a>                          02-Jan-2024 10:11                   -", "-")]
	[InlineData("<a href=\"x\">x</a> 02-Jan-2024 10:11 5 ", "5")]
	public void Nginx_KnownRows(string line, string expectedSize)
	{
		Assert.True(LineRegexFastPath.TryMatchNginx(line, out string size));
		Assert.Equal(expectedSize, size);
		AssertNginxClaimIsRight(line);
	}

	[Theory]
	[InlineData("<img src=\"/icons/unknown.gif\" alt=\"[   ]\"> <a href=\"file.mkv\">file.mkv</a>      2024-01-02 10:11  1.4G   ", "1.4G", "")]
	[InlineData("<img src=\"/icons/folder.gif\" alt=\"[DIR]\"> <a href=\"d/\">d/</a>                  2024-01-02 10:11    -   ", "-", "")]
	[InlineData("<img src=\"/i.gif\" alt=\"[   ]\"> <a href=\"f\">f</a>  2024-01-02 10:11  123  a description here", "123", "a description here")]
	public void Apache_KnownRows(string line, string expectedSize, string expectedDescription)
	{
		Assert.True(LineRegexFastPath.TryMatchApache(line, out string size, out string description));
		Assert.Equal(expectedSize, size);
		Assert.Equal(expectedDescription, description);
		AssertApacheClaimIsRight(line);
	}

	[Theory]
	[InlineData("<a href=\"x\">x</a>")]
	[InlineData("<a href=\"x\">x</a> 02-Jan-2024 10:11")]
	[InlineData("<a href=\"x\">x</a> 02-Jan-2024 10:11 100 extra token")]
	[InlineData("<a href=\"x\">x</a> 02-Jan-2024  10:11 100")]
	[InlineData("<a href=\"x\">x</a> 02-Jan-2024 10:11:30 100")]
	[InlineData("<A HREF=\"x\">x</A> 02-Jan-2024 10:11 100")]
	[InlineData("<a href=\"x\">x</a> <b>y</b> 02-Jan-2024 10:11 100")]
	public void Nginx_UnusualRows_AreLeftToTheRegex(string line)
	{
		Assert.False(LineRegexFastPath.TryMatchNginx(line, out _));
	}

	[Theory]
	[InlineData("<img src=\"x\"> <a href=\"f\">f</a> 2024-01-02 10:11:30 123")]
	[InlineData("<img src=\"x\"> <a href=\"f\">f</a>x 2024-01-02 10:11 123")]
	[InlineData("<img src=\"x\"> <b>y</b> <a href=\"f\">f</a> 2024-01-02 10:11 123")]
	[InlineData("<img src=\"x\"> <a href=\"f\">f</a>")]
	[InlineData("<img src=\"x\"> <a href=\"f\">f</a> no date here 123")]
	[InlineData("<a href=\"f\">f</a> 2024-01-02 10:11 123")]
	public void Apache_UnusualRows_AreLeftToTheRegex(string line)
	{
		Assert.False(LineRegexFastPath.TryMatchApache(line, out _, out _));
	}
}
