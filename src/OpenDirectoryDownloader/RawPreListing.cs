namespace OpenDirectoryDownloader;

/// <summary>
/// For a big directory listing, AngleSharp's DOM of the page is mostly nodes for the thousands of lines inside its
/// one &lt;pre&gt; (about 1.6 KB per file for nginx, 3.9 KB for Apache), only to serialize them straight back to text
/// and split them into lines. When the page has exactly one plain &lt;pre&gt; and every line in it is strictly
/// simple (see LineFragmentParser.IsStrictListingLine), that text can be read from the raw HTML, and the page parsed
/// with the &lt;pre&gt; emptied. Nothing in the rest of the parser can look inside such a &lt;pre&gt;: its lines hold
/// only links and images with href, src, alt and title.
///
/// This is only a shortcut. Whenever the page is not that shape, or the shortcut gives no result, the caller
/// parses the whole page the normal way, from the start; RawPreListingTests runs every sample page both ways.
/// </summary>
public static class RawPreListing
{
	/// <summary>Marks the emptied &lt;pre&gt; in the DOM, so the caller can tell it is really that element (and not text in a script or comment).</summary>
	public const string MarkerAttribute = "data-odd-raw";

	private const string MarkedPre = "<pre " + MarkerAttribute + "=\"1\"></pre>";

	/// <summary>Pages shorter than this are always parsed the normal way: the shortcut only pays off for big listings.</summary>
	public const int DefaultMinimumLength = 200_000;

	private static readonly AsyncLocal<int?> MinimumLengthOverride = new();

	/// <summary>For tests: use the shortcut for every page (0) or none (int.MaxValue) within the current async context.</summary>
	public static int MinimumLength
	{
		get => MinimumLengthOverride.Value ?? DefaultMinimumLength;
		set => MinimumLengthOverride.Value = value;
	}

	public sealed record Extracted(string HtmlWithMarkedPre, List<string> Lines);

	public static Extracted TryExtract(string html)
	{
		int open = html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase);

		// Exactly one "<pre", and it is a plain <pre> without attributes
		if (open < 0 || string.CompareOrdinal(html, open, "<pre>", 0, 5) != 0 || html.IndexOf("<pre", open + 4, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return null;
		}

		int contentStart = open + 5;
		int close = html.IndexOf("</pre", StringComparison.OrdinalIgnoreCase);

		// Exactly one "</pre", after the open tag, and it is a plain </pre>
		if (close < contentStart || string.CompareOrdinal(html, close, "</pre>", 0, 6) != 0 || html.IndexOf("</pre", close + 5, StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return null;
		}

		ReadOnlySpan<char> content = html.AsSpan(contentStart, close - contentStart);

		// The HTML parser first turns CRLF and CR into LF (nginx and Windows servers end every line with CRLF), then
		// drops one line feed straight after <pre>
		if (content.StartsWith("\r\n"))
		{
			content = content[2..];
		}
		else if (content.StartsWith("\n") || content.StartsWith("\r"))
		{
			content = content[1..];
		}

		if (content.IndexOf('&') >= 0)
		{
			return null;
		}

		List<string> lines = [];
		int lineStart = 0;
		int position = 0;

		while (position < content.Length)
		{
			int separatorLength = 0;

			if (content[position] == '\r' && content[position..].StartsWith("\r\n"))
			{
				separatorLength = 2;
			}
			else if (content[position] is '\n' or '\r')
			{
				separatorLength = 1;
			}
			else if (content[position] == '<' && content[position..].StartsWith("<hr>"))
			{
				separatorLength = 4;
			}

			if (separatorLength == 0)
			{
				position++;
				continue;
			}

			if (!TryAddLine(lines, content[lineStart..position]))
			{
				return null;
			}

			position += separatorLength;
			lineStart = position;
		}

		if (!TryAddLine(lines, content[lineStart..]))
		{
			return null;
		}

		string markedHtml = string.Concat(html.AsSpan(0, open), MarkedPre, html.AsSpan(close + 6));

		return new Extracted(markedHtml, lines);
	}

	private static bool TryAddLine(List<string> lines, ReadOnlySpan<char> line)
	{
		string text = line.ToString();

		if (!LineFragmentParser.IsStrictListingLine(text))
		{
			return false;
		}

		lines.Add(text);

		return true;
	}
}
