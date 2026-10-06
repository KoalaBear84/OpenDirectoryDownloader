using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Html;
using OpenDirectoryDownloader.Urls;
using System.Net;
using System.Text;

namespace OpenDirectoryDownloader.Parsing.Listings.Tables;

/// <summary>The generic parser for listings in a &lt;table&gt;: every table on the page is tried, the best one wins.</summary>
internal static class TableListingParser
{
	public const string Parser = "ParseTablesDirectoryListing";

	public static WebDirectory Parse(string baseUrl, WebDirectory parsedWebDirectory, IHtmlCollection<IElement> tables, bool checkParents, ILogger logger)
	{
		// Dirty solution..
		bool hasSeperateDirectoryAndFilesTables = false;

		List<(WebDirectory Directory, int HeaderCount)> results = [];

		foreach (IElement table in tables)
		{
			WebDirectory webDirectoryCopy = parsedWebDirectory.Clone();
			webDirectoryCopy.ParentDirectory = parsedWebDirectory.ParentDirectory;

			Dictionary<int, HeaderInfo> tableHeaders = TableHeaders.Get(table);
			int headerCount = tableHeaders.Count(th => th.Value.Type != HeaderType.Unknown);

			KeyValuePair<int, HeaderInfo> fileSizeHeader = tableHeaders.FirstOrDefault(th => th.Value.Type == HeaderType.FileSize);
			int fileSizeHeaderColumnIndex = fileSizeHeader.Value != null ? fileSizeHeader.Key : 0;
			int nameHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.FileName);
			int descriptionHeaderColumnIndex = TableHeaders.ColumnOf(tableHeaders, HeaderType.Description);

			// Extra fallback
			if (fileSizeHeaderColumnIndex != 0 && nameHeaderColumnIndex == 0)
			{
				foreach (IElement tableRow in table.QuerySelectorAll("tbody tr"))
				{
					if (nameHeaderColumnIndex != 0 || tableRow.QuerySelectorAll("a").Length != 1)
					{
						continue;
					}

					foreach (IElement tableColumn in tableRow.QuerySelectorAll("td"))
					{
						if (tableColumn.QuerySelector("a") == null)
						{
							continue;
						}

						nameHeaderColumnIndex = tableColumn.Index();
						break;
					}
				}
			}

			if (fileSizeHeaderColumnIndex == 0 && nameHeaderColumnIndex == 0)
			{
				if (table.QuerySelector("a") != null)
				{
					webDirectoryCopy = LinkListingParser.ParseLinks(baseUrl, webDirectoryCopy, table.QuerySelectorAll("a"), checkParents, logger);
				}
			}
			else
			{
				webDirectoryCopy.ParsedSuccessfully = true;

				// The same selector strings for every row
				string nameCellSelector = $"td:nth-child({nameHeaderColumnIndex})";
				string folderIconSelector = $"{nameCellSelector} i.bi-folder";
				string descriptionCellSelector = $"td:nth-child({descriptionHeaderColumnIndex})";
				string sizeCellSelector = $"td:nth-child({fileSizeHeaderColumnIndex})";

				foreach (IElement tableRow in table.QuerySelectorAll("tbody tr"))
				{
					if (tableRow.QuerySelector("img[alt=\"[ICO]\"]") != null ||
					    tableRow.QuerySelector("img[alt=\"[PARENTDIR]\"]") != null ||
					    tableRow.QuerySelector("a") == null ||
					    tableRow.QuerySelector("th") != null ||
					    tableRow.ClassList.Contains("snHeading") ||
					    tableRow.QuerySelector(nameCellSelector) is not IElement nameCell ||
					    nameCell.TextContent.Contains("parent directory", StringComparison.InvariantCultureIgnoreCase) ||
					    tableRow.QuerySelector("table") != null)
					{
						continue;
					}

					bool addedEntry = false;

					foreach (IElement link in tableRow.QuerySelectorAll("a"))
					{
						if (addedEntry || !ListingHelpers.IsValidLink(link))
						{
							continue;
						}

						addedEntry = true;

						ListingHelpers.ProcessUrl(baseUrl, link, out string linkHref, out Uri uri, out string fullUrl);

						fullUrl = ListingPostProcessor.StripUrl(fullUrl);

						bool hasFolderIcon = tableRow.QuerySelector(folderIconSelector) != null;
						UrlEncodingParser urlEncodingParser = new(fullUrl);

						IElement imageElement = tableRow.QuerySelector("img");
						string imageFileName = imageElement?.HasAttribute("src") == true ? Path.GetFileName(imageElement.Attributes["src"].Value) : null;

						bool isDirectory = hasFolderIcon || tableRow.ClassList.Contains("dir") ||
						                   (imageElement != null &&
						                    (
							                    (imageElement.HasAttribute("alt") &&
							                     imageElement.Attributes["alt"].Value == "[DIR]") ||
							                    (imageFileName is not null &&
							                     (imageFileName.Contains("dir") ||
							                      imageFileName.Contains("folder"))) ||
							                    urlEncodingParser["dirname"] != null
						                    ));

						string description = tableRow.QuerySelector(descriptionCellSelector)?.TextContent.Trim();
						string size = tableRow.QuerySelector(sizeCellSelector)?.TextContent.Trim().Replace(" ", string.Empty);

						size ??= tableRow.QuerySelector(".size")?.TextContent.Trim();

						bool isFile = urlEncodingParser["file"] != null ||
							!isDirectory &&
							(urlEncodingParser["dir"] == null && (
								(fileSizeHeader.Value == null && !linkHref.EndsWith("/")) ||
								(ListingHelpers.IsFileSize(size) && size != "0.00b" && !string.IsNullOrWhiteSpace(size) &&
								 (size?.Contains("item")).Value != true && !linkHref.EndsWith("/"))
							));

						if (!isFile)
						{
							string directoryName = WebUtility.UrlDecode(Path.GetDirectoryName(uri.Segments.Last()));

							// Fallback..
							if (string.IsNullOrWhiteSpace(directoryName))
							{
								directoryName = uri.Segments.Last();
							}

							if (urlEncodingParser["dir"] != null)
							{
								hasSeperateDirectoryAndFilesTables = true;
								directoryName = link.TextContent.Trim();
							}

							if (urlEncodingParser["directory"] != null)
							{
								directoryName = link.TextContent.Trim();
							}

							if (urlEncodingParser["dirname"] != null || hasFolderIcon)
							{
								directoryName = link.TextContent.Trim();
							}

							if (urlEncodingParser["folder"] != null)
							{
								if (UrlHelper.IsBase64String(urlEncodingParser["folder"]))
								{
									directoryName = Encoding.UTF8.GetString(Convert.FromBase64String(urlEncodingParser["folder"]));
								}
								else
								{
									directoryName = link.TextContent.Trim();
								}
							}

							if (link.ClassList.Contains("name"))
							{
								directoryName = link.TextContent.Trim();

								if (directoryName.StartsWith(".."))
								{
									continue;
								}
							}

							webDirectoryCopy.Subdirectories.Add(new WebDirectory(webDirectoryCopy)
							{
								Parser = Parser,
								Url = fullUrl,
								Name = WebUtility.UrlDecode(directoryName),
								Description = description
							});
						}
						else
						{
							webDirectoryCopy.Parser = Parser;

							string filename = Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath));

							if (urlEncodingParser["url"] != null)
							{
								filename = Path.GetFileName(WebUtility.UrlDecode(new Uri(urlEncodingParser["url"]).AbsolutePath));
							}

							if (urlEncodingParser["file"] != null)
							{
								filename = Path.GetFileName(WebUtility.UrlDecode(urlEncodingParser["file"]));
							}

							if (string.IsNullOrWhiteSpace(filename))
							{
								filename = link.TextContent;
							}

							if (link.ClassList.Contains("name"))
							{
								filename = link.TextContent.Trim();
							}

							if (urlEncodingParser.Count == 0 && filename.Equals("index.php", StringComparison.InvariantCultureIgnoreCase))
							{
								continue;
							}

							webDirectoryCopy.Files.Add(new WebFile
							{
								Url = fullUrl,
								FileName = filename,
								FileSize = FileSizeHelper.ParseFileSize(size),
								Description = description
							});
						}
					}
				}
			}

			results.Add((webDirectoryCopy, headerCount));
		}

		if (!hasSeperateDirectoryAndFilesTables)
		{
			parsedWebDirectory = results.Where(r => (r.Directory.ParsedSuccessfully || r.Directory.Error) && (r.Directory.Files.Count > 0 || r.Directory.Subdirectories.Count > 0))
				                     .OrderByDescending(r => r.HeaderCount)
				                     .ThenByDescending(r => r.Directory.TotalDirectoriesIncludingUnfinished + r.Directory.TotalFiles)
				                     .Select(r => r.Directory)
				                     .FirstOrDefault() ?? parsedWebDirectory;
		}
		else
		{
			parsedWebDirectory.Subdirectories = [.. results.SelectMany(r => r.Directory.Subdirectories)];
			parsedWebDirectory.Files = [.. results.SelectMany(r => r.Directory.Files)];
		}

		ListingPostProcessor.CheckParsedResults(parsedWebDirectory, baseUrl, checkParents, logger);

		return parsedWebDirectory;
	}
}
