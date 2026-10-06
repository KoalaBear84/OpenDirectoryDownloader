using AngleSharp.Dom;
using OpenDirectoryDownloader.Helpers;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Listings.Tables;

public enum HeaderType
{
	Unknown,
	FileName,
	FileSize,
	Modified,
	Description,
	Type
}

[DebuggerDisplay("{Type,nq}, {Header,nq}")]
internal sealed class HeaderInfo
{
	public string Header { get; set; }
	public HeaderType Type { get; set; } = HeaderType.Unknown;
}

/// <summary>Works out which column of a listing table holds the name, size, date, ... from its headers, or from its contents when it has no usable headers.</summary>
internal static partial class TableHeaders
{
	/// <summary>The headers of <paramref name="table"/> by 1-based column index.</summary>
	public static Dictionary<int, HeaderInfo> Get(IElement table)
	{
		Dictionary<int, HeaderInfo> tableHeaders = [];

		IHtmlCollection<IElement> headers = table.QuerySelector("th")?.ParentElement?.QuerySelectorAll("th");

		bool removeFirstRow = false;

		if (headers != null && headers.First().HasAttribute("colspan"))
		{
			headers = null;
		}

		headers ??= table.QuerySelector(".snHeading")?.QuerySelectorAll("td");

		if (headers == null || headers.Length == 0)
		{
			headers = table.QuerySelectorAll("thead td, thead th");
		}

		if (headers.Length == 0)
		{
			headers = table.QuerySelectorAll("tr:nth-child(1) > th");
		}

		if (headers.Length == 0)
		{
			headers = table.QuerySelectorAll("tr:nth-child(1) > td");

			if (headers?.Length > 0)
			{
				removeFirstRow = true;
			}
		}

		if (headers?.Any() != true)
		{
			return tableHeaders;
		}

		int headerIndex = 1;

		foreach (IElement header in headers)
		{
			if (header.QuerySelector("table") != null)
			{
				continue;
			}

			HeaderInfo tableHeaderInfo = GetHeaderInfo(header);

			tableHeaders.Add(headerIndex, tableHeaderInfo);

			if (header.HasAttribute("colspan"))
			{
				headerIndex += int.Parse(header.GetAttribute("colspan")) - 1;
			}

			headerIndex++;
		}

		// Dynamically guess column types
		if (tableHeaders.All(th => th.Value.Type == HeaderType.Unknown))
		{
			tableHeaders.Clear();

			List<int> fileNameColumnIndex = [];
			List<int> dateColumnIndex = [];
			List<int> fileSizeColumnIndex = [];
			List<int> typeColumnIndex = [];

			int maxColumns = 0;

			foreach (IElement tableRow in table.QuerySelectorAll("tr"))
			{
				List<IElement> rowColumns = tableRow.QuerySelectorAll("td").ToList();

				maxColumns = Math.Max(maxColumns, rowColumns.Count);

				foreach (IElement tableColumn in rowColumns)
				{
					if (tableColumn.QuerySelector("a") != null)
					{
						fileNameColumnIndex.Add(tableColumn.Index());
					}

					if (DateTime.TryParse(tableColumn.TextContent, out DateTime parsedDateTime) && parsedDateTime != DateTime.MinValue)
					{
						dateColumnIndex.Add(tableColumn.Index());
					}

					if (FileSizeHelper.ParseFileSize(tableColumn.TextContent, onlyChecking: true) is not null)
					{
						fileSizeColumnIndex.Add(tableColumn.Index());
					}

					if (tableColumn.QuerySelector("img") != null)
					{
						typeColumnIndex.Add(tableColumn.Index());
					}
				}
			}

			AddGuessedColumn(tableHeaders, fileNameColumnIndex, maxColumns, HeaderType.FileName);
			AddGuessedColumn(tableHeaders, dateColumnIndex, maxColumns, HeaderType.Modified);
			AddGuessedColumn(tableHeaders, fileSizeColumnIndex, maxColumns, HeaderType.FileSize);
			AddGuessedColumn(tableHeaders, typeColumnIndex, maxColumns, HeaderType.Type);
		}
		else
		{
			if (tableHeaders.Any(th => th.Value.Type == HeaderType.FileName) &&
			    tableHeaders.Any(th => th.Value.Type == HeaderType.FileSize) && removeFirstRow)
			{
				table.QuerySelector("tr:nth-child(1)")?.Remove();
			}
		}

		IElement firstRow = table.QuerySelector("tbody tr");

		// Correct colspans when using in tbody
		if (firstRow is null)
		{
			return tableHeaders;
		}

		List<IElement> columns = firstRow.QuerySelectorAll("td").ToList();

		for (int i = 0; i < columns.Count; i++)
		{
			IElement tableColumn = columns[i];

			if (!tableColumn.HasAttribute("colspan"))
			{
				continue;
			}

			int colspan = int.Parse(tableColumn.GetAttribute("colspan"));

			foreach (KeyValuePair<int, HeaderInfo> tableHeader in tableHeaders.Where(header => header.Key > i + colspan).ToList())
			{
				HeaderInfo headerInfo = tableHeaders[tableHeader.Key];
				tableHeaders.Remove(tableHeader.Key);

				int newKey = tableHeader.Key - (colspan - 1);

				tableHeaders.TryAdd(newKey, headerInfo);
			}
		}

		return tableHeaders;
	}

	private static void AddGuessedColumn(Dictionary<int, HeaderInfo> tableHeaders, List<int> columnIndexes, int maxColumns, HeaderType type)
	{
		if (columnIndexes.Count == 0)
		{
			return;
		}

		int columnIndex = ((int)Math.Round(columnIndexes.Average())) + 1;
		columnIndex = Math.Min(columnIndex, maxColumns);

		tableHeaders.TryAdd(columnIndex, new HeaderInfo { Type = type });
	}

	/// <summary>The 1-based column index of the first header of <paramref name="type"/>, or 0.</summary>
	public static int ColumnOf(Dictionary<int, HeaderInfo> tableHeaders, HeaderType type)
	{
		KeyValuePair<int, HeaderInfo> header = tableHeaders.FirstOrDefault(th => th.Value.Type == type);

		return header.Value != null ? header.Key : 0;
	}

	public static HeaderInfo GetHeaderInfo(IElement header)
	{
		string headerName = header.TextContent.Trim();

		HeaderInfo headerInfo = new()
		{
			Header = headerName
		};

		// Someone thought it would be a good idea to include it in a table..
		if (header.QuerySelector("ul") != null)
		{
			return headerInfo;
		}

		headerName = headerName.ToLowerInvariant();

		headerName = HeaderNameCleanupRegex().Replace(headerName, string.Empty);

		if (headerName == "lastmodified" || headerName == "modified" || headerName.Contains("date") ||
		    headerName.Contains("lastmodification") || headerName.Contains("time") || headerName.Contains("修改时间") ||
		    headerName.Contains("修改日期") || headerName.Contains("最終更新"))
		{
			headerInfo.Type = HeaderType.Modified;
		}

		if (headerName == "type")
		{
			headerInfo.Type = HeaderType.Type;
		}

		if (headerName.Contains("size") || headerName.Contains("file size") || headerName.Contains("filesize") ||
		    headerName.Contains("taille") ||
		    // Creates a problem for a single testcase, need to find a better way
		    //headerName.Contains("größe") ||
		    headerName.Contains("大小") ||
		    headerName.Contains("サイズ")
		   )
		{
			headerInfo.Type = HeaderType.FileSize;
		}

		if (headerName == "description")
		{
			headerInfo.Type = HeaderType.Description;
		}

		// Check this as last one because of generic 'file' in it..
		if (
			headerInfo.Type == HeaderType.Unknown &&
			(
				headerName == "file" ||
				headerName.Contains("name") ||
				headerName.Contains("file name") ||
				headerName.Contains("filename") ||
				headerName == "directory" ||
				headerName.Contains("link") ||
				headerName.Contains("nom") ||
				headerName.Contains("文件") ||
				headerName.Contains("ファイル名")
			)
		)
		{
			headerInfo.Type = HeaderType.FileName;
		}

		return headerInfo;
	}

	[GeneratedRegex(@"[^¿-῿Ⰰ-퟿\w]")]
	private static partial Regex HeaderNameCleanupRegex();
}
