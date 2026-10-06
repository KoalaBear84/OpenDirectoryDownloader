using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Html;
using OpenDirectoryDownloader.Urls;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Listings.Tables;

/// <summary>dirLIST, https://sourceforge.net/projects/dirlist/</summary>
internal static class DirListParser
{
	public const string Parser = "ParseDirLIST";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> tables)
	{
		if (!htmlDocument.Title.StartsWith("dirLIST - Index of:"))
		{
			return parsedWebDirectory;
		}

		List<IElement> dirListTables = tables.Where(t => t.GetAttribute("width") == "725").ToList();

		if (dirListTables.Any(t => t.TextContent.Contains("No files or folders in this directory")))
		{
			parsedWebDirectory.Parser = Parser;
			parsedWebDirectory.ParsedSuccessfully = true;
			return parsedWebDirectory;
		}

		if (dirListTables.Count < 3)
		{
			return parsedWebDirectory;
		}

		parsedWebDirectory.ParsedSuccessfully = true;

		IHtmlCollection<IElement> entries = dirListTables[3].QuerySelectorAll("td.folder_bg, td.file_bg1, td.file_bg2");

		foreach (IElement entry in entries)
		{
			if (entry.QuerySelector("a") is not IHtmlAnchorElement link)
			{
				continue;
			}

			bool isDirectory = entry.Matches("td.folder_bg");

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			if (isDirectory)
			{
				UrlEncodingParser urlEncodingParser = new(fullUrl);

				string directoryName = UrlHelper.IsBase64String(urlEncodingParser["folder"]) ?
					Encoding.UTF8.GetString(Convert.FromBase64String(urlEncodingParser["folder"])) :
					link.TextContent.Trim();

				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = Parser,
					Url = fullUrl,
					Name = directoryName
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fullUrl)),
					FileSize = null
				});
			}
		}

		return parsedWebDirectory;
	}
}

/// <summary>Snif, http://web.archive.org/web/20140724000351/http://bitfolge.de/index.php?option=com_content&amp;view=article&amp;id=66:snif-simple-and-nice-index-file&amp;catid=38:eigene&amp;Itemid=59</summary>
internal static partial class SnifListingParser
{
	public const string Parser = "ParseSnifDirectoryListing";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> snifTableRows, bool checkParents, ILogger logger)
	{
		IElement table = snifTableRows.First().Parent("table");

		Dictionary<int, HeaderInfo> tableHeaders = TableHeaders.Get(table);
		int fileSizeHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileSize);
		int nameHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileName);

		foreach (IElement tableRow in snifTableRows)
		{
			if (tableRow.ClassList.Contains("snHeading") || tableRow.QuerySelector("td").ClassList.Contains("snDir"))
			{
				continue;
			}

			IHtmlAnchorElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a") as IHtmlAnchorElement;

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();
			bool isFile = ListingHelpers.IsFileSize(size) && !size.Contains("item");

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = Parser,
					Url = fullUrl,
					Name = link?.Title
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = link?.Title,
					FileSize = long.Parse(string.Join(null, RegexNonDigits().Split((tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex}) span") as IHtmlSpanElement).Title)))
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}

	[GeneratedRegex(@"[^\d]")]
	private static partial Regex RegexNonDigits();
}

/// <summary>Godir, https://gitlab.com/Montessquio/godir</summary>
internal static class GodirListingParser
{
	public const string Parser = "ParsePureDirectoryListing";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, IHtmlCollection<IElement> pureTableRows, bool checkParents, ILogger logger)
	{
		string urlFromBreadcrumbs = Uri.EscapeDataString(string.Join("/", htmlDocument.QuerySelectorAll(".breadcrumbs_main .breadcrumb").Where(b => !b.ClassList.Contains("smaller")).Select(b => b.TextContent)) + "/");

		// Remove possible file part (index.html) from url
		if (!string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(baseUrl))))
		{
			baseUrl = baseUrl.Replace(Path.GetFileName(WebUtility.UrlDecode(baseUrl)), string.Empty);
		}

		// /. is for problem displaying a folder starting with a dot
		string urlFromBaseUrl = baseUrl.Remove(0, new Uri(baseUrl).Scheme.Length + new Uri(baseUrl).Host.Length + 3).Replace("/.", "/");
		urlFromBaseUrl = urlFromBaseUrl.Replace("%23", "#");

		if (urlFromBreadcrumbs == urlFromBaseUrl || urlFromBreadcrumbs == Uri.EscapeDataString(urlFromBaseUrl))
		{
			IElement table = pureTableRows.First().Parent("table");

			Dictionary<int, HeaderInfo> tableHeaders = TableHeaders.Get(table);
			int fileSizeHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileSize);
			int nameHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileName);

			foreach (IElement tableRow in pureTableRows)
			{
				IElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a");

				if (!ListingHelpers.IsValidLink(link))
				{
					continue;
				}

				string linkHref = link.TextContent;
				Uri uri = new(new Uri(baseUrl), Uri.EscapeDataString(linkHref));
				string fullUrl = uri.ToString();
				bool isFile = !tableRow.ClassList.Contains("dir");

				if (!isFile)
				{
					if (fullUrl.Contains('#'))
					{
						fullUrl = fullUrl.Replace("#", "%23");
					}

					parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
					{
						Parser = Parser,
						Url = $"{fullUrl}/",
						Name = link.TextContent
					});
				}
				else
				{
					parsedWebDirectory.Files.Add(new WebFile
					{
						Url = fullUrl,
						FileName = link.TextContent,
						FileSize = FileSizeHelper.ParseFileSize(tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})").TextContent)
					});
				}
			}
		}
		else
		{
			parsedWebDirectory.Error = true;
			logger.LogError("Directory listing returns different directory than requested! Expected: {urlFromBaseUrl}, Actual: {urlFromBreadcrumbs}", urlFromBaseUrl, urlFromBreadcrumbs);
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}

/// <summary>h5ai without JavaScript, https://larsjung.de/h5ai/</summary>
internal static class H5aiListingParser
{
	public const string Parser = "ParseH5aiDirectoryListing";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> h5aiTableRows, bool checkParents, ILogger logger)
	{
		IElement table = h5aiTableRows[0].Parent("table");

		Dictionary<int, HeaderInfo> tableHeaders = TableHeaders.Get(table);
		int fileSizeHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileSize);
		int nameHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileName);

		foreach (IElement tableRow in h5aiTableRows)
		{
			IHtmlAnchorElement link = tableRow.QuerySelector($"td:nth-child({nameHeaderColumnIndex})")?.QuerySelector("a") as IHtmlAnchorElement;

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			string size = tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})")?.TextContent.Trim();
			bool isFile = !string.IsNullOrWhiteSpace(size);
			IElement image = tableRow.QuerySelector("img");

			if (isFile && image != null && image.HasAttribute("alt") && image.Attributes["alt"]?.Value == "folder")
			{
				isFile = false;
			}

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = Parser,
					Url = fullUrl,
					Name = link?.TextContent.Trim()
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = link?.TextContent.Trim(),
					FileSize = FileSizeHelper.ParseFileSize(tableRow.QuerySelector($"td:nth-child({fileSizeHeaderColumnIndex})").TextContent)
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}

/// <summary>The IPFS gateway's directory listing</summary>
internal static class IpfsListingParser
{
	public const string Parser = "ParseIpfsDirectoryListing";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, bool checkParents, ILogger logger)
	{
		foreach (IElement tableRow in htmlDocument.QuerySelectorAll("table tr"))
		{
			IElement link = tableRow.QuerySelector("a");

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			string size = tableRow.QuerySelector("td:nth-child(3)")?.TextContent.Trim();
			bool isFile = ListingHelpers.IsFileSize(size);

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = Parser,
					Url = fullUrl,
					Name = link?.TextContent
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}
