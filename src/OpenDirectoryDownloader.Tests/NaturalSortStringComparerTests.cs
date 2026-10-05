using OpenDirectoryDownloader.Helpers;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class NaturalSortStringComparerTests
{
	[Theory]
	[InlineData("file2.txt", "file10.txt", -1)]
	[InlineData("file10.txt", "file2.txt", 1)]
	[InlineData("a", "a", 0)]
	[InlineData("file001.txt", "file1.txt", -1)]
	[InlineData("http://h/dir-02/file-00042.mkv", "http://h/dir-02/file-00043.mkv", -1)]
	[InlineData("abc", "abcd", -1)]
	public void SortsNaturally(string x, string y, int expectedSign)
	{
		Assert.Equal(expectedSign, Math.Sign(NaturalSortStringComparer.InvariantCulture.Compare(x, y)));

		// The expectations above describe the original behaviour, which the optimized comparer must keep
		Assert.Equal(expectedSign, Math.Sign(ReferenceNaturalSortStringComparer.InvariantCulture.Compare(x, y)));
	}

	private static string RandomString(Random random, string alphabet, int maxLength)
	{
		int length = random.Next(0, maxLength + 1);
		char[] chars = new char[length];

		for (int i = 0; i < length; i++)
		{
			chars[i] = alphabet[random.Next(alphabet.Length)];
		}

		return new string(chars);
	}

	[Theory]
	[InlineData(StringComparison.Ordinal)]
	[InlineData(StringComparison.OrdinalIgnoreCase)]
	[InlineData(StringComparison.InvariantCulture)]
	[InlineData(StringComparison.InvariantCultureIgnoreCase)]
	public void AgreesWithTheOriginalImplementation_OnRandomStrings(StringComparison comparison)
	{
		NaturalSortStringComparer actual = new(comparison);
		ReferenceNaturalSortStringComparer reference = new(comparison);
		Random random = new(12345);

		// Digits, letters in both cases, accents, separators, an Arabic-Indic digit (IsDigit but not ASCII) and leading zeros
		const string alphabet = "0000123456789aAbBéÉz/.-_ :\u0663";

		for (int i = 0; i < 150_000; i++)
		{
			// Mostly short strings (lots of equal prefixes), some long ones with 19+ digit runs (long overflow)
			string x = RandomString(random, alphabet, i % 10 == 0 ? 40 : 8);
			string y = random.Next(3) == 0 ? x + RandomString(random, alphabet, 4) : RandomString(random, alphabet, i % 10 == 0 ? 40 : 8);

			Assert.True(Math.Sign(reference.Compare(x, y)) == Math.Sign(actual.Compare(x, y)), $"Different result for '{x}' vs '{y}' ({comparison})");
		}
	}

	[Fact]
	public void AgreesWithTheOriginalImplementation_OnUrlsWithALongCommonPrefix()
	{
		NaturalSortStringComparer actual = NaturalSortStringComparer.InvariantCulture;
		ReferenceNaturalSortStringComparer reference = ReferenceNaturalSortStringComparer.InvariantCulture;
		Random random = new(7);
		string[] hosts = ["http://127.0.0.1:57645/", "http://127.0.0.1:5764/", "https://files.example.test/", "http://127.0.0.1:57645/movies/"];

		string Url() => $"{hosts[random.Next(hosts.Length)]}dir-{random.Next(0, 120):D2}/file-{random.Next(0, 100_000):D5}.{(random.Next(2) == 0 ? "mkv" : "txt")}";

		for (int i = 0; i < 100_000; i++)
		{
			string x = Url();
			string y = random.Next(10) == 0 ? x : Url();

			Assert.True(Math.Sign(reference.Compare(x, y)) == Math.Sign(actual.Compare(x, y)), $"Different result for '{x}' vs '{y}'");
		}
	}

	[Fact]
	public void SortingBigUrlList_GivesTheSameOrderAsTheOriginal_AndIsFast()
	{
		Random random = new(99);
		List<string> urls = [.. Enumerable.Range(0, 100_000).Select(i => $"http://127.0.0.1:57645/movies/dir-{i / 5000:D2}/file-{i:D5}.mkv").OrderBy(_ => random.Next())];

		List<string> expected = [.. urls];
		expected.Sort(ReferenceNaturalSortStringComparer.InvariantCulture);

		List<string> actual = [.. urls];
		System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
		actual.Sort(NaturalSortStringComparer.InvariantCulture);
		stopwatch.Stop();

		Assert.Equal(expected, actual);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Sorting 100,000 URLs took {stopwatch.Elapsed.TotalSeconds:F1} s (was ~3 s before the optimization)");
	}
}
