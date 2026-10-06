using OpenDirectoryDownloader.Shared.Models;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// Every sample page parsed and dumped in full (parser, order, names, sizes, descriptions), compared against a dump
/// made with the parser before the architecture rebuild. Set ODD_WRITE_GOLDEN=1 to rewrite the golden files.
/// </summary>
public class GoldenParseTests
{
	public GoldenParseTests()
	{
		Program.Logger ??= new Serilog.LoggerConfiguration().CreateLogger();
	}

	private static string GoldenDirectory([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path), "Golden");

	public static IEnumerable<TheoryDataRow<string>> Samples()
	{
		foreach (string path in Directory.EnumerateFiles("Samples", "*.dat").Order(StringComparer.Ordinal))
		{
			yield return new TheoryDataRow<string>(Path.GetFileName(path));
		}
	}

	private static string Describe(WebDirectory directory)
	{
		StringBuilder text = new();
		text.Append($"url={directory.Url} name={directory.Name} desc={directory.Description} parser={directory.Parser} error={directory.Error} ok={directory.ParsedSuccessfully}\n");

		foreach (WebDirectory subdirectory in directory.Subdirectories)
		{
			text.Append($"  dir {subdirectory.Url} | {subdirectory.Name} | {subdirectory.Description} | {subdirectory.Parser}\n");
		}

		foreach (WebFile file in directory.Files)
		{
			text.Append($"  file {file.Url} | {file.FileName} | {file.FileSize} | {file.Description}\n");
		}

		return text.ToString();
	}

	private static async Task<string> DumpAsync(string html)
	{
		StringBuilder text = new();

		try
		{
			text.Append("== root\n");
			text.Append(Describe(await DirectoryParser.ParseHtml(new WebDirectory(null) { Url = "http://localhost/" }, html)));
		}
		catch (Exception ex)
		{
			text.Append("EXCEPTION " + ex.GetType().Name + "\n");
		}

		try
		{
			text.Append("== nested\n");
			WebDirectory directory = new(new WebDirectory(null) { Url = "http://localhost:8086/", Name = "root" }) { Url = "http://localhost:8086/listing/", Name = "listing" };
			text.Append(Describe(await DirectoryParser.ParseHtml(directory, html)));
		}
		catch (Exception ex)
		{
			text.Append("EXCEPTION " + ex.GetType().Name + "\n");
		}

		return text.ToString();
	}

	[Theory]
	[MemberData(nameof(Samples))]
	public async Task Sample_ParsesExactlyAsBefore(string sample)
	{
		string html = await File.ReadAllTextAsync(Path.Combine("Samples", sample), TestContext.Current.CancellationToken);
		string actual = await DumpAsync(html);
		string goldenPath = Path.Combine(GoldenDirectory(), sample + ".txt");

		if (Environment.GetEnvironmentVariable("ODD_WRITE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(GoldenDirectory());
			await File.WriteAllTextAsync(goldenPath, actual, TestContext.Current.CancellationToken);
			return;
		}

		Assert.True(File.Exists(goldenPath), $"No golden file for {sample}");
		string expected = (await File.ReadAllTextAsync(goldenPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

		if (expected != actual)
		{
			string[] expectedLines = expected.Split('\n');
			string[] actualLines = actual.Split('\n');
			int line = 0;

			while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
			{
				line++;
			}

			Assert.Fail($"{sample} differs at line {line + 1}{Environment.NewLine}expected: {(line < expectedLines.Length ? expectedLines[line] : "<end>")}{Environment.NewLine}actual:   {(line < actualLines.Length ? actualLines[line] : "<end>")}");
		}
	}
}
