using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Listings.Tables;
using OpenDirectoryDownloader.Urls;
using System.Net;

namespace OpenDirectoryDownloader.Parsing.Listings;

/// <summary>Listings drawn with &lt;div&gt;s: an unknown one with &lt;div id="listing"&gt;, one with &lt;div id="filelist"&gt; rows, and HFS 2.4+.</summary>
internal static class DivListingParsers
{
	public static WebDirectory ParseCustomDivListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> divElements, bool checkParents, ILogger logger)
	{
		foreach (IElement divElement in divElements)
		{
			string size = divElement.QuerySelector("em")?.TextContent.Trim();
			IElement link = divElement.QuerySelector("a");

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			bool isFile = ListingHelpers.IsFileSize(size);

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseCustomDivListing",
					Url = fullUrl,
					Name = link.QuerySelector("strong").TextContent.TrimEnd('/')
				});
			}
			else
			{
				UrlEncodingParser urlEncodingParser = new(fullUrl);

				string fileName = new Uri(fullUrl).AbsolutePath;

				if (urlEncodingParser["download"] != null)
				{
					fileName = urlEncodingParser["download"];
				}

				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fileName)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}

	public static WebDirectory ParseCustomDivListing2(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> divElements, bool checkParents, ILogger logger)
	{
		foreach (IElement divElement in divElements)
		{
			bool isFile = divElement.ClassList.Contains("afile");

			IElement link = divElement.QuerySelector("a");

			if (link is null)
			{
				continue;
			}

			if (!isFile)
			{
				string linkHref = link.Attributes["data-href"]?.Value;
				link.SetAttribute("href", linkHref);
			}

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			Uri uri = new(new Uri(baseUrl), link.Attributes["href"]?.Value);
			string fullUrl = uri.ToString();

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseCustomDivListing2",
					Url = fullUrl,
					Name = link.TextContent
				});
			}
			else
			{
				string fileName = new Uri(fullUrl).AbsolutePath;
				string size = divElement.QuerySelector(".sz")?.TextContent.Trim();

				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fileName)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}

	public static WebDirectory ParseHfsListing(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> divElements, bool checkParents, ILogger logger)
	{
		foreach (IElement divElement in divElements)
		{
			IElement link = divElement.QuerySelector("a");

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out Uri uri, out string fullUrl);

			bool isFile = !divElement.ClassList.Contains("item-type-folder");

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = "ParseHfsListing",
					Url = fullUrl,
					Name = WebUtility.UrlDecode(uri.Segments.Last()).Trim().TrimEnd(ListingHelpers.TrimChars)
				});
			}
			else
			{
				string fileName = new Uri(fullUrl).AbsolutePath;
				string size = divElement.QuerySelector(".item-size")?.TextContent.Trim();

				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = Path.GetFileName(WebUtility.UrlDecode(fileName)),
					FileSize = FileSizeHelper.ParseFileSize(size)
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}

/// <summary>directory-listing.com style &lt;li&gt; listings</summary>
internal static class DirectoryListingDotComParser
{
	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool checkParents, ILogger logger)
	{
		foreach (IElement listItem in listItems)
		{
			IElement link = listItem.QuerySelector("a");

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			string size = listItem.QuerySelector(".file-size")?.TextContent.Trim();
			bool isFile = ListingHelpers.IsFileSize(size);

			if (!isFile)
			{
				if (listItem.Attributes["data-name"]?.Value != "..")
				{
					parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
					{
						Parser = "ParseDirectoryListingDoctComDirectoryListing",
						Url = fullUrl,
						Name = listItem.Attributes["data-name"]?.Value
					});
				}
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

/// <summary>Material design (mdui) &lt;li&gt; listings, used by several Google Drive/OneDrive index front ends</summary>
internal static class MaterialDesignListingParser
{
	public const string Parser = "ParseMaterialDesignListItemsDirectoryListing";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> listItems, bool checkParents, ILogger logger)
	{
		int nameIndex = -1;
		int sizeIndex = -1;
		int modifiedIndex = -1;

		IElement headerListItem = listItems.First(li => li.ClassList.Contains("th"));

		int columnIndex = 0;

		foreach (IElement div in headerListItem.QuerySelectorAll("div"))
		{
			IElement italicElement = div.QuerySelector("i");

			IAttr dataSortAttribute = italicElement?.Attributes["data-sort"];

			if (dataSortAttribute != null)
			{
				switch (dataSortAttribute.Value)
				{
					case "name":
						nameIndex = columnIndex;
						break;
					case "date":
						modifiedIndex = columnIndex;
						break;
					case "size":
						sizeIndex = columnIndex;
						break;
				}
			}

			IElement linkElement = div.QuerySelector("a");

			if (linkElement != null)
			{
				UrlEncodingParser urlEncodingParser = new(linkElement.Attributes["href"]?.Value);

				string sortBy = urlEncodingParser["sortby"];

				switch (sortBy)
				{
					case "name":
						nameIndex = columnIndex;
						break;
					case "lastModtime":
						modifiedIndex = columnIndex;
						break;
					case "size":
						sizeIndex = columnIndex;
						break;
				}
			}

			if (div.Children.Length == 0)
			{
				switch (TableHeaders.GetHeaderInfo(div).Type)
				{
					case HeaderType.FileName:
						nameIndex = columnIndex;
						break;
					case HeaderType.FileSize:
						sizeIndex = columnIndex;
						break;
					case HeaderType.Modified:
						modifiedIndex = columnIndex;
						break;
				}
			}

			columnIndex++;
		}

		foreach (IElement listItem in listItems.Where(li => li.ClassList.Contains("mdui-list-item") && !li.ClassList.Contains("th")))
		{
			IElement italicItem = listItem.QuerySelector("i");

			if (italicItem != null && italicItem.TextContent.Trim() == "arrow_upward")
			{
				continue;
			}

			foreach (IElement removeItem in listItem.QuerySelectorAll("i"))
			{
				removeItem.Remove();
			}

			IElement link = listItem.QuerySelector("a");

			if (link?.Attributes["href"] == null)
			{
				continue;
			}

			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			string name = string.Empty;

			IElement nameElement = listItem.QuerySelector(".mdui-text-truncate");

			if (nameElement != null)
			{
				name = nameElement.TextContent.Trim();
			}

			if (listItem.Attributes["data-sort-name"] != null)
			{
				name = listItem.Attributes["data-sort-name"].Value;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				name = link.QuerySelector($"div:nth-child({nameIndex + 1})")?.TextContent.Trim();
			}

			// Read for the row shape, the date is not stored
			_ = link.QuerySelector($"div:nth-child({modifiedIndex + 1})")?.TextContent.Trim();

			if (listItem.Attributes["data-sort-date"] != null)
			{
				_ = UrlHelper.UnixTimestampToDateTime(long.Parse(listItem.Attributes["data-sort-date"].Value)).ToString();
			}

			string fileSize = link.QuerySelector($"div:nth-child({sizeIndex + 1})")?.TextContent.Trim();

			if (listItem.Attributes["data-sort-size"] != null)
			{
				fileSize = listItem.Attributes["data-sort-size"].Value;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out _, out string fullUrl);

			bool isFile = listItem.ClassList.Contains("file");

			if (!isFile)
			{
				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = Parser,
					Url = fullUrl,
					Name = name
				});
			}
			else
			{
				parsedWebDirectory.Files.Add(new WebFile
				{
					Url = fullUrl,
					FileName = name,
					FileSize = FileSizeHelper.ParseFileSize(fileSize)
				});
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}

/// <summary>Directory Lister, https://www.directorylister.com/ (version 1 to 3 and the newer Alpine.js based one)</summary>
internal static class DirectoryListerParser
{
	public const string Parser01 = "ParseDirectoryLister01DirectoryListing";
	public const string Parser02 = "ParseDirectoryLister02DirectoryListing";

	public static WebDirectory Parse01(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, bool checkParents, ILogger logger)
	{
		return Parse(baseUrl, parsedWebDirectory, Parser01, htmlDocument, "#content > div > div > div", "#content ul#file-list li", checkParents, logger);
	}

	public static WebDirectory Parse02(string baseUrl, WebDirectory parsedWebDirectory, IHtmlDocument htmlDocument, bool checkParents, ILogger logger)
	{
		return Parse(baseUrl, parsedWebDirectory, Parser02, htmlDocument, "body > div[x-data=\"application\"] > div > div > div > div > div", "body > div[x-data=\"application\"] > div > div > div > ul > li", checkParents, logger);
	}

	private static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, string parser, IHtmlDocument htmlDocument, string headerSelector, string listItemSelector, bool checkParents, ILogger logger)
	{
		// Set first: when the page is not quite the expected shape, the error below is reported for this parser
		parsedWebDirectory.Parser = parser;

		IHtmlCollection<IElement> headerDivs = htmlDocument.QuerySelectorAll(headerSelector);

		List<HeaderInfo> tableHeaderInfos = [.. headerDivs.Select(TableHeaders.GetHeaderInfo)];

		// The headers don't change per row: look them up once per page
		int fileNameHeaderIndex = tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileName);
		int fileSizeHeaderIndex = tableHeaderInfos.FindIndex(h => h.Type == HeaderType.FileSize);

		IHtmlCollection<IElement> links = htmlDocument.QuerySelectorAll(listItemSelector).Last().QuerySelectorAll("a");

		foreach (IElement link in links)
		{
			if (!ListingHelpers.IsValidLink(link))
			{
				continue;
			}

			ListingHelpers.ProcessUrl(baseUrl, link, out _, out Uri uri, out string fullUrl);

			bool isFile = !link.QuerySelector("i").ClassList.Contains("fa-folder");

			if (!isFile)
			{
				UrlEncodingParser urlEncodingParser = new(fullUrl);

				parsedWebDirectory.Subdirectories.Add(new WebDirectory(parsedWebDirectory)
				{
					Parser = parser,
					// Will fix URLs which ends in spaces etc
					Url = uri.AbsoluteUri,
					Name = WebUtility.UrlDecode(urlEncodingParser["dir"]?.Split("/").Last()),
				});
			}
			else
			{
				try
				{
					List<IElement> divs = link.QuerySelectorAll("div > div").ToList();
					string fileSize = divs.Skip(2).ToList()[fileSizeHeaderIndex].TextContent;
					// The result is unused, kept so a missing FileName header still throws here (row dropped, as before)
					divs.RemoveAt(fileNameHeaderIndex + 1);

					parsedWebDirectory.Files.Add(new WebFile
					{
						Url = fullUrl,
						FileName = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath)),
						FileSize = FileSizeHelper.ParseFileSize(fileSize),
					});
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error parsing with ParseDirectoryListerDirectoryListing");
				}
			}
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}
