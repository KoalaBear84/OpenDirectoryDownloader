using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OpenDirectoryDownloader.Tests.Integration;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// The ten line regexes of DirectoryParser use RegexOptions.ExplicitCapture, so their unnamed groups (which
/// nothing reads) are no longer captured: that is fewer objects per matched line. Matching itself must not
/// change, so each regex is compared with the same pattern without that option: same success, same named groups.
/// </summary>
public class LineRegexTests
{
	private static readonly Regex LineSeparator = new(@"\r\n|\r|\n|<br\S*>|<hr>");

	private static List<Regex> GeneratedRegexes() =>
		[.. Enumerable.Range(1, 10).Select(i => (Regex)typeof(DirectoryParser).GetMethod($"RegexRegexParser{i}", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!)];

	private static List<string> CorpusLines()
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

		foreach (ListingStyle style in new[] { ListingStyle.ApachePre, ListingStyle.NginxAutoindex })
		{
			FakeSite site = new(new FakeSiteOptions { Style = style, Depth = 1, SubdirectoriesPerDirectory = 3, FilesPerDirectory = 300 });
			IElement pre = new HtmlParser().ParseDocument(site.RenderListing("/dir-01/")).QuerySelector("pre")!;
			lines.AddRange(LineSeparator.Split(pre.FastInnerHtml()));
		}

		return lines;
	}

	private static string Mutate(Random random, string line)
	{
		const string alphabet = "0123456789abcXYZ -:/<>\"=.,_;()[]& ";
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

	private static void AssertSameMatch(int parser, Regex generated, Regex reference, string line)
	{
		Match actual = generated.Match(line);
		Match expected = reference.Match(line);

		Assert.True(expected.Success == actual.Success, $"Regex {parser}: different success for line: {line}");

		if (!expected.Success)
		{
			return;
		}

		Assert.True(expected.Value == actual.Value, $"Regex {parser}: different match for line: {line}");

		foreach (string name in generated.GetGroupNames().Where(n => !int.TryParse(n, out _)))
		{
			Assert.True(expected.Groups[name].Success == actual.Groups[name].Success && expected.Groups[name].Value == actual.Groups[name].Value,
				$"Regex {parser}: different group '{name}' for line: {line}");
		}
	}

	[Fact]
	public void EveryLineRegex_MatchesLikeItDidWithoutExplicitCapture_OnRealAndMutatedLines()
	{
		List<Regex> generated = GeneratedRegexes();
		List<Regex> references = [.. generated.Select(r => new Regex(r.ToString(), RegexOptions.None))];
		Random random = new(77);

		// Sample the corpus (distinct lines, deterministic) so this stays quick: the real lines and their mutations still cover every regex
		List<string> corpus = [.. CorpusLines().Distinct().OrderBy(_ => random.Next()).Take(700)];
		int matchedAtLeastOnce = 0;

		Assert.True(corpus.Count >= 500, $"Expected a real corpus, only {corpus.Count} lines");

		for (int parser = 0; parser < generated.Count; parser++)
		{
			int matches = 0;

			foreach (string line in corpus)
			{
				AssertSameMatch(parser + 1, generated[parser], references[parser], line);
				matches += generated[parser].IsMatch(line) ? 1 : 0;

				// mutated variants of the same line: near misses are where the two could disagree
				for (int variant = 0; variant < 1; variant++)
				{
					AssertSameMatch(parser + 1, generated[parser], references[parser], Mutate(random, line));
				}
			}

			matchedAtLeastOnce += matches > 0 ? 1 : 0;
		}

		Assert.True(matchedAtLeastOnce >= 7, $"The corpus should exercise most of the regexes, only {matchedAtLeastOnce} matched anything");
	}

	[Fact]
	public void EveryLineRegex_HasNoUnnamedCapturingGroupsLeft()
	{
		foreach (Regex regex in GeneratedRegexes())
		{
			Assert.All(regex.GetGroupNames(), name => Assert.True(name == "0" || !int.TryParse(name, out _), $"Unnamed group {name} in {regex}"));
		}
	}
}
