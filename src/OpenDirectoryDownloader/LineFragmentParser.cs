using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace OpenDirectoryDownloader;

/// <summary>
/// Parses one line of HTML (a row of a &lt;pre&gt;-style directory listing) into just enough DOM for RegexParser1..10
/// to run QuerySelector against.
///
/// A big listing is hundreds of thousands of such lines, and AngleSharp costs ~20 µs and a lot of garbage per
/// call (a whole document for the shell plus a full tokenizer and tree builder for the fragment), which made
/// the line parsing about half of the time to scan a big directory. Almost every line is the same simple
/// shape, so those are built directly: lowercase &lt;a&gt; and &lt;img&gt; with plain double-quoted attributes and
/// plain text. Anything else (entities, other tags, odd quoting, ...) goes through AngleSharp as before, so
/// the result is the same DOM either way; LineFragmentParserTests compares both paths on every sample listing.
/// </summary>
public static class LineFragmentParser
{
	private static readonly HtmlParser HtmlParser = new();

	// AngleSharp documents are not thread safe, and the crawler parses on many threads: one shell per thread
	[ThreadStatic]
	private static IHtmlDocument _shellDocument;

	private static IHtmlDocument ShellDocument => _shellDocument ??= HtmlParser.ParseDocument(string.Empty);

	public static IElement Parse(string line)
	{
		return TryParseSimple(line) ?? ParseWithAngleSharp(line);
	}

	/// <summary>The general path: AngleSharp's own fragment parser.</summary>
	public static IElement ParseWithAngleSharp(string line)
	{
		IElement container = ShellDocument.CreateElement("div");

		// ParseFragment's result is live against the nodes it just produced, so it must be snapshotted
		// (ToArray) before AppendChild mutates things out from under the in-progress enumeration.
		foreach (INode node in HtmlParser.ParseFragment(line, container).ToArray())
		{
			container.AppendChild(node);
		}

		return container;
	}

	/// <summary>The same DOM for the simple shape, or null when the line is anything else (the caller then uses AngleSharp).</summary>
	public static IElement TryParseSimple(string line)
	{
		IHtmlDocument document = ShellDocument;
		IElement container = document.CreateElement("div");
		int position = 0;

		while (position < line.Length)
		{
			int tagStart = line.IndexOf('<', position);
			int textEnd = tagStart < 0 ? line.Length : tagStart;

			if (textEnd > position)
			{
				string text = line.Substring(position, textEnd - position);

				if (!IsPlainText(text))
				{
					return null;
				}

				container.AppendChild(document.CreateTextNode(text));
			}

			if (tagStart < 0)
			{
				break;
			}

			position = tagStart + 1;
			int nameStart = position;

			while (position < line.Length && line[position] is >= 'a' and <= 'z')
			{
				position++;
			}

			string tagName = line.Substring(nameStart, position - nameStart);

			if (tagName is not ("a" or "img"))
			{
				return null;
			}

			IElement element = document.CreateElement(tagName);

			if (!TryReadAttributes(line, ref position, element))
			{
				return null;
			}

			if (tagName == "a")
			{
				int closeStart = line.IndexOf('<', position);

				if (closeStart < 0 || string.CompareOrdinal(line, closeStart, "</a>", 0, 4) != 0)
				{
					return null;
				}

				string linkText = line.Substring(position, closeStart - position);

				if (!IsPlainText(linkText))
				{
					return null;
				}

				if (linkText.Length > 0)
				{
					element.AppendChild(document.CreateTextNode(linkText));
				}

				position = closeStart + 4;
			}

			container.AppendChild(element);
		}

		return container;
	}

	/// <summary>Reads ` name="value"` pairs up to the closing '>'. Anything not strictly that shape fails, leaving it to AngleSharp.</summary>
	private static bool TryReadAttributes(string line, ref int position, IElement element)
	{
		HashSet<string> seen = null;

		while (true)
		{
			if (position >= line.Length)
			{
				return false;
			}

			if (line[position] == '>')
			{
				position++;

				return true;
			}

			if (line[position] != ' ')
			{
				return false;
			}

			position++;

			int nameStart = position;

			while (position < line.Length && (line[position] is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
			{
				position++;
			}

			if (position == nameStart || position + 1 >= line.Length || line[position] != '=' || line[position + 1] != '"')
			{
				return false;
			}

			string name = line.Substring(nameStart, position - nameStart);
			int valueStart = position + 2;
			int valueEnd = line.IndexOf('"', valueStart);

			if (valueEnd < 0)
			{
				return false;
			}

			string value = line.Substring(valueStart, valueEnd - valueStart);

			// The HTML parser keeps the first of a duplicate attribute; SetAttribute would keep the last
			if (!IsPlainText(value) || value.Contains('\'') || value.Contains('>') || !(seen ??= []).Add(name))
			{
				return false;
			}

			element.SetAttribute(name, value);
			position = valueEnd + 1;

			// Attributes need whitespace between them; anything else is parse-error recovery territory
			if (position >= line.Length || (line[position] != ' ' && line[position] != '>'))
			{
				return false;
			}
		}
	}

	/// <summary>No entities (they would need decoding), no characters the HTML parser rewrites, no tag start.</summary>
	private static bool IsPlainText(string text)
	{
		foreach (char c in text)
		{
			if (c is '&' or '<' or '\0' or '\r' or '\n' or '\f' or '\t' || char.IsSurrogate(c))
			{
				return false;
			}
		}

		return true;
	}
}
