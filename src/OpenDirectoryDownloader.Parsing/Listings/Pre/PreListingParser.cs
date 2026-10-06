using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Html;
using OpenDirectoryDownloader.Urls;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Listings.Pre;

/// <summary>
/// Listings in a &lt;pre&gt; (Apache, nginx, IIS, lighttpd, ...): every line is tried against the line parsers in
/// order and the first that recognizes it wins. The order matters, a later parser would read some lines differently.
/// </summary>
public static partial class PreListingParser
{
	public const string Parser = "ParsePreDirectoryListing";

	private delegate bool LineParser(WebDirectory webDirectory, string baseUrl, string line, ILogger logger);

	private static readonly LineParser[] LineParsers =
	[
		RegexParser1,
		RegexParser2,
		RegexParser3,
		RegexParser4,
		RegexParser5,
		RegexParser6,
		RegexParser7,
		RegexParser8,
		RegexParser9,
		RegexParser10,
	];

	internal static WebDirectory ParsePres(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> pres, bool checkParents, ILogger logger)
	{
		// Lazily, one <pre> at a time
		return ParseLines(baseUrl, parsedWebDirectory, pres.Select(pre => pre.FastPreLines() ?? RegexPreLineSeparator().Split(pre.FastInnerHtml()).ToList()), checkParents, logger);
	}

	internal static WebDirectory ParseLines(string baseUrl, WebDirectory parsedWebDirectory, IEnumerable<List<string>> linesPerPre, bool checkParents, ILogger logger)
	{
		foreach (List<string> lines in linesPerPre)
		{
			foreach (string line in lines)
			{
				foreach (LineParser lineParser in LineParsers)
				{
					if (!lineParser(parsedWebDirectory, baseUrl, line, logger))
					{
						continue;
					}

					parsedWebDirectory.Parser = Parser;
					break;
				}
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}

	[GeneratedRegex(@"(?:<img.*>\s*)+<a.*?>.*?<\/a>\S*\s*(?<Modified>\d*-(?:[a-zA-Z]*|\d*)-\d*\s*\d*:\d*(:\d*)?)?\s*(?<FileSize>\S+)?(\s*(?<Description>.*))?", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser1();

	private static bool RegexParser1(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		// Regular Apache rows are read directly (no Match and groups per line); anything else goes through the regex as before
		if (!LineRegexFastPath.TryMatchApache(line, out string fileSizeText, out string descriptionText))
		{
			Match match = RegexRegexParser1().Match(line);

			if (!match.Success)
			{
				return false;
			}

			fileSizeText = match.Groups["FileSize"].Value;
			descriptionText = match.Groups["Description"].Value;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (parsedLine.HasImage("[ICO]") ||
			parsedLine.HasImage("[PARENTDIR]") ||
			!parsedLine.HasLink ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return true;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return true;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = ListingHelpers.IsFileSize(fileSizeText.Trim()) && !parsedLine.HasImage("[DIR]");

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser1",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
				Description = descriptionText.Trim()
			});
		}
		else
		{
			try
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(fileSizeText),
					Description = descriptionText.Trim()
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser1");
			}
		}

		return true;
	}

	[GeneratedRegex(@"<a.*<\/a>\s*(?<DateTime>\d+-\w+-\d+\s\d+:\d{0,2}|-)\s*(?<FileSize>\S+\s?\S*)?\s*\S*", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser2();

	private static bool RegexParser2(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		// Regular nginx rows are read directly (no Match and groups per line); anything else goes through the regex as before
		if (!LineRegexFastPath.TryMatchNginx(line, out string fileSizeGroup))
		{
			Match match = RegexRegexParser2().Match(line);

			if (!match.Success)
			{
				return false;
			}

			fileSizeGroup = match.Groups["FileSize"].Value.Trim();
		}

		LineView parsedLine = LineFragmentParser.Parse(line);
		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return true;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);


		bool isFile = long.TryParse(fileSizeGroup, out long parsedFileSize);
		long? fileSize = parsedFileSize;

		if (!isFile && ListingHelpers.IsFileSize(fileSizeGroup))
		{
			fileSize = FileSizeHelper.ParseFileSize(fileSizeGroup);
			isFile = fileSize is not null;
		}

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser2",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()))
			});
		}
		else
		{
			webDirectory.Files.Add(new WebFile
			{
				Url = fullUrl,
				FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
				FileSize = fileSize
			});
		}

		return true;
	}

	[GeneratedRegex(@"(?<Modified>\d+[\.-](?:[a-zA-Z]*|\d+)[\.-]\d+(?:\s*\d*:\d*(?::\d*)?)?)(?:<img.*>\s*)?\S*\s*(?<FileSize>\S+)\s*?<[aA].*<\/[aA]>", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser3();

	private static bool RegexParser3(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser3().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (parsedLine.HasImage("[ICO]") ||
			parsedLine.HasImage("[PARENTDIR]") ||
			!parsedLine.HasLink ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;" && match.Groups["FileSize"].Value.Trim() != "DIR";

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser3",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
				//Description = match.Groups["Description"].Value.Trim()
			});
		}
		else
		{
			try
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
					//Description = match.Groups["Description"].Value.Trim()
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser3");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"\s*(?<Modified>[A-z]*,\s*[A-z]*\s*\d*, \d*\s*\d*:\d*\s*[APM]*)\s+(?<FileSize>\S*)\s+<a.*<\/a>", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser4();

	private static bool RegexParser4(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser4().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (parsedLine.HasImage("[ICO]") ||
			parsedLine.HasImage("[PARENTDIR]") ||
			!parsedLine.HasLink ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser4",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
				//Description = match.Groups["Description"].Value.Trim()
			});
		}
		else
		{
			try
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
					//Description = match.Groups["Description"].Value.Trim()
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser4");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"\s*(?<Modified>\d*-\d*-\d*\s*[오전후]*\s*\d*:\d*)\s*(?<FileSize>\S*)\s+<[aA].*<\/[aA]>", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser5();

	private static bool RegexParser5(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser5().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (parsedLine.HasImage("[ICO]") ||
			parsedLine.HasImage("[PARENTDIR]") ||
			!parsedLine.HasLink ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser5",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
				//Description = match.Groups["Description"].Value.Trim()
			});
		}
		else
		{
			try
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
					//Description = match.Groups["Description"].Value.Trim()
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser5");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"(?<Modified>\d+\/\d+\/\d+(\s*\d+:\d+\s+[APM]+)?)\s+(?<FileSize>\S*)\s*<a.*<\/a>", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser6();

	private static bool RegexParser6(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser6().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (parsedLine.HasImage("[ICO]") ||
			parsedLine.HasImage("[PARENTDIR]") ||
			!parsedLine.HasLink ||
			line.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase))
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = match.Groups["FileSize"].Value.Trim() != "&lt;dir&gt;";

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser6",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last())),
				//Description = match.Groups["Description"].Value.Trim()
			});
		}
		else
		{
			try
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(match.Groups["FileSize"].Value),
					//Description = match.Groups["Description"].Value.Trim()
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser6");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"(?i)(?<FileMode>[d-]r[-w][x-])\s*\d*\s*(?<FileSize>-?\d*)\s*(\S{3}\s*\d*\s*(?:\d*:\d*(:\d*)?|\d*\.?))\s*(<a.*<\/a>\/?)", RegexOptions.ExplicitCapture, "en-NL")]
	private static partial Regex RegexRegexParser7();

	private static bool RegexParser7(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser7().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (!parsedLine.HasLink)
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		string fileMode = match.Groups["FileMode"].Value.ToLowerInvariant();
		bool isFile = !fileMode.StartsWith('d') && !fileMode.StartsWith('l');

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser7",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()))
			});
		}
		else
		{
			try
			{
				string fileSize = match.Groups["FileSize"].Value;

				if (fileSize.StartsWith('-'))
				{
					// If filesize is negative, it will be 4GB minus the amount of bytes (without the - sign),
					// but this will only work for when it is between 2 and 4 GB, so skip it
					fileSize = string.Empty;
				}

				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(fileSize)
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser7");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"^\s*(?<Link><a.*<\/a>)\s+(?:(?<Day>\d+)(?<Month>\D+)(?<Year>\d+))(?:\s+(?<Hour>\d+):(?<Minute>\d+))(?:\s+)?(?<FileSize>\S+)?", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser8();

	private static bool RegexParser8(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser8().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (!parsedLine.HasLink)
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		string fileSizeString = match.Groups["FileSize"].Value;
		long? fileSize = FileSizeHelper.ParseFileSize(fileSizeString);

		bool isFile = !string.IsNullOrWhiteSpace(fileSizeString) && fileSizeString.Trim() != "-" &&
			            // This is not perfect.. Specific block sizes
			            (fileSize is not 32768 or 65536);

		if (!isFile)
		{
			string directoryName = Path.GetDirectoryName(WebUtility.UrlDecode(uri.Segments.Last()));

			if (string.IsNullOrWhiteSpace(directoryName))
			{
				directoryName = WebUtility.UrlDecode(linkHref);
			}

			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser8",
				Url = fullUrl,
				Name = directoryName
			});
		}
		else
		{
			try
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = fileSize
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser8");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"^\s*(?<Link><a.*<\/a>)\s*(?<IsDirectory>\/?)(?<FileSize>\S+)?", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser9();

	private static bool RegexParser9(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser9().Match(line);

		if (!match.Success ||
			(match.Groups["IsDirectory"].Success &&
			    !string.IsNullOrWhiteSpace(match.Groups["IsDirectory"].Value)) ==
			match.Groups["FileSize"].Success)
		{
			return match.Success;
		}

		if (match.Groups["FileSize"].Value.Contains('<'))
		{
			return false;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (!parsedLine.HasLink)
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		bool isFile = !string.IsNullOrWhiteSpace(match.Groups["FileSize"].Value) &&
			            match.Groups["FileSize"].Value.Trim() != "-";

		if (!isFile)
		{
			string directoryName = Path.GetDirectoryName(WebUtility.UrlDecode(uri.Segments.Last()));

			if (string.IsNullOrWhiteSpace(directoryName))
			{
				directoryName = WebUtility.UrlDecode(linkHref);
			}

			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser9",
				Url = fullUrl,
				Name = directoryName
			});
		}
		else
		{
			try
			{
				string fileSize = match.Groups["FileSize"].Value;

				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(fileSize)
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser9");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"(?<Dir>[\-ld])(?<Permissions>(?:[\-r][\-w][\-xs]){1,3})\s+(?<Owner>\w+)\s+(?<Group>\w+)\s+(?<Month>\S{3})\s+(?<Day>\d+)\s+(?<Year>\d+)\s+(?<FileSize>\d+\s+?\w+)?\s+(?:[\w&;]+\s+)?(?<Link><a.*<\/a>\/?)?", RegexOptions.ExplicitCapture)]
	private static partial Regex RegexRegexParser10();

	private static bool RegexParser10(WebDirectory webDirectory, string baseUrl, string line, ILogger logger)
	{
		Match match = RegexRegexParser10().Match(line);

		if (!match.Success)
		{
			return match.Success;
		}

		LineView parsedLine = LineFragmentParser.Parse(line);

		if (!parsedLine.HasLink)
		{
			return match.Success;
		}

		LineView link = parsedLine;

		if (!ListingHelpers.IsValidLink(link))
		{
			return match.Success;
		}

		UrlHelper.ProcessUrl(baseUrl, link.Href, out string linkHref, out Uri uri, out string fullUrl);

		string fileMode = match.Groups["Dir"].Value.ToLowerInvariant();

		bool isFile = !fileMode.StartsWith('d') && !fileMode.StartsWith('l');

		if (!isFile)
		{
			webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
			{
				Parser = "RegexParser10",
				Url = fullUrl,
				Name = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()))
			});
		}
		else
		{
			try
			{
				string fileSize = match.Groups["FileSize"].Value;

				webDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = UrlHelper.GetFileNameFromUrl(uri, fullUrl),
					FileSize = FileSizeHelper.ParseFileSize(fileSize)
				});
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error parsing with RegexParser10");
			}
		}

		return match.Success;
	}

	[GeneratedRegex(@"\r\n|\r|\n|<br\S*>|<hr>")]
	private static partial Regex RegexPreLineSeparator();
}
