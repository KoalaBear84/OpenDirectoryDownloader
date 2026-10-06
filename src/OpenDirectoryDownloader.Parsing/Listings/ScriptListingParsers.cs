using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Listings;

/// <summary>Listings drawn by JavaScript calls like _d('name','date','link') and _f(...), read straight from the page source.</summary>
internal static partial class JavaScriptDrawnParser
{
	public const string Parser = "ParseJavaScriptDrawn";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, string html)
	{
		// Both patterns start with "_d('" or "_f('", most pages have neither: skip the two regex scans of the whole page then
		if (!html.Contains("_d('", StringComparison.Ordinal) && !html.Contains("_f('", StringComparison.Ordinal))
		{
			return parsedWebDirectory;
		}

		MatchCollection matchCollectionDirectories = RegexDirectory().Matches(html);
		MatchCollection matchCollectionFiles = RegexFile().Matches(html);

		if (matchCollectionDirectories.Count == 0 && matchCollectionFiles.Count == 0)
		{
			return parsedWebDirectory;
		}

		parsedWebDirectory.ParsedSuccessfully = true;

		foreach (Match directory in matchCollectionDirectories)
		{
			// Remove possible file part (index.php) from url
			if (!string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(baseUrl))))
			{
				UrlEncodingParser urlEncodingParser = new(baseUrl);

				foreach (string key in urlEncodingParser.AllKeys)
				{
					urlEncodingParser.Remove(key);
				}

				baseUrl = urlEncodingParser.ToString();

				baseUrl = new Uri(baseUrl.Replace(Path.GetFileName(new Uri(baseUrl).AbsolutePath), string.Empty)).ToString();
			}

			parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
			{
				Parser = Parser,
				Url = baseUrl + directory.Groups["Link"].Value,
				Name = Uri.UnescapeDataString(directory.Groups["DirectoryName"].Value)
			});
		}

		foreach (Match file in matchCollectionFiles)
		{
			parsedWebDirectory.Files.Add(new WebFile
			{
				Url = baseUrl + Path.GetFileName(WebUtility.UrlDecode(Uri.UnescapeDataString(file.Groups["Link"].Value))),
				FileName = Path.GetFileName(WebUtility.UrlDecode(Uri.UnescapeDataString(file.Groups["FileName"].Value))),
				FileSize = FileSizeHelper.ParseFileSize(file.Groups["FileSize"].Value)
			});
		}

		return parsedWebDirectory;
	}

	[GeneratedRegex(@"_d\('(?<DirectoryName>.*)','(?<Date>.*)','(?<Link>.*)'\)")]
	private static partial Regex RegexDirectory();

	[GeneratedRegex(@"_f\('(?<FileName>.*)',(?<FileSize>\d*),'(?<Date>.*)','(?<Link>.*)',(?<UnixTimestamp>\d*)\)")]
	private static partial Regex RegexFile();
}

/// <summary>An unknown listing that loads its whole tree as JSON (div.filemanager with a script.js doing $.get('...')).</summary>
public static partial class DirectoryListingModel01Parser
{
	public const string Parser = "DirectoryListingModel01";

	/// <returns>Whether the page was this listing and its result replaced <paramref name="parsedWebDirectory"/>'s content</returns>
	internal static async Task<bool> TryParseAsync(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, HttpClient httpClient)
	{
		IElement fileManager = htmlDocument.QuerySelector("div.filemanager");

		if (fileManager == null)
		{
			return false;
		}

		IElement script = htmlDocument.QuerySelector("script[src*=\"script.js\"]");

		if (script == null)
		{
			return false;
		}

		Uri scriptUri = new(new Uri(baseUrl), script.Attributes["src"].Value);
		string scriptBody = await httpClient.GetStringAsync(scriptUri);

		Match regexMatch = RegexDirectoryIndexFile().Match(scriptBody);

		if (!regexMatch.Success)
		{
			return false;
		}

		Uri directoryIndexFile = new(new Uri(baseUrl), regexMatch.Groups["DirectoryIndexFile"].Value);

		string directoryIndexJson = await httpClient.GetStringAsync(directoryIndexFile);

		DirectoryListingModel01 directoryListingModel = JsonSerializer.Deserialize(directoryIndexJson, ListingJsonContext.Default.DirectoryListingModel01);

		WebDirectory newWebDirectory = Convert(baseUrl, parsedWebDirectory, directoryListingModel);
		parsedWebDirectory.Description = newWebDirectory.Description;
		parsedWebDirectory.Files = newWebDirectory.Files;
		parsedWebDirectory.Finished = newWebDirectory.Finished;
		parsedWebDirectory.Name = newWebDirectory.Name;
		parsedWebDirectory.Subdirectories = newWebDirectory.Subdirectories;
		parsedWebDirectory.Url = newWebDirectory.Url;
		parsedWebDirectory.ParsedSuccessfully = true;
		parsedWebDirectory.Parser = Parser;

		return true;
	}

	private static WebDirectory Convert(string baseUrl, WebDirectory parsedWebDirectory, DirectoryListingModel01 directoryListingModel)
	{
		Uri directoryUri = new(new Uri(baseUrl), directoryListingModel.Path);
		string directoryFullUrl = directoryUri.ToString();

		WebDirectory webDirectory = new(parsedWebDirectory.ParentDirectory)
		{
			Url = directoryFullUrl,
			Name = directoryListingModel.Name
		};

		foreach (DirectoryListingModel01 item in directoryListingModel.Items)
		{
			Uri uri = new(new Uri(baseUrl), item.Path);
			string itemFullUrl = uri.ToString();

			if (item.Type == "folder")
			{
				WebDirectory newWebDirectory = Convert(baseUrl, webDirectory, item);
				webDirectory.Subdirectories.Add(newWebDirectory);
			}
			else
			{
				webDirectory.Files.Add(new WebFile
				{
					Url = itemFullUrl,
					FileName = Path.GetFileName(itemFullUrl),
					FileSize = item.Size
				});
			}
		}

		return webDirectory;
	}

	[GeneratedRegex(@"\$\.get\('(?<DirectoryIndexFile>.*)',")]
	private static partial Regex RegexDirectoryIndexFile();
}
