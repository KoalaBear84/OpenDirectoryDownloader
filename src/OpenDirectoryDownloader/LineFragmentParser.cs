using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace OpenDirectoryDownloader;

/// <summary>
/// What the line parsers (RegexParser1..10) need to know about one line of a &lt;pre&gt;-style directory listing:
/// its first link (href, title, text) and which images it contains. Nothing else is ever asked of the line's
/// HTML, so there is no need to build a DOM for it (AngleSharp elements cost 2.5 to 5 KB per line, and a big
/// listing has hundreds of thousands of lines).
/// </summary>
public sealed class LineView
{
	private string _alt0;
	private string _alt1;
	private List<string> _moreAlts;

	/// <summary>True when the line has an &lt;a&gt; element (<c>QuerySelector("a") != null</c>).</summary>
	public bool HasLink { get; init; }

	/// <summary>The first link's href attribute, null when it has none.</summary>
	public string Href { get; init; }

	/// <summary>The first link's title attribute, null when it has none or there is no link (what IHtmlAnchorElement.Title returns).</summary>
	public string Title { get; init; }

	/// <summary>The first link's text content, null when there is no link.</summary>
	public string Text { get; init; }

	/// <summary>True when the line has an &lt;img&gt; whose alt attribute is exactly <paramref name="alt"/> (<c>QuerySelector("img[alt=...]") != null</c>).</summary>
	public bool HasImage(string alt)
	{
		return _alt0 == alt || _alt1 == alt || (_moreAlts is not null && _moreAlts.Contains(alt));
	}

	internal void AddImageAlt(string alt)
	{
		if (alt is null)
		{
			return;
		}

		if (_alt0 is null)
		{
			_alt0 = alt;
		}
		else if (_alt1 is null)
		{
			_alt1 = alt;
		}
		else
		{
			(_moreAlts ??= []).Add(alt);
		}
	}

	/// <summary>Everything the parsers can observe, as one string, for comparing two ways of reading the same line.</summary>
	public override string ToString()
	{
		string alts = string.Join("|", new[] { _alt0, _alt1 }.Concat(_moreAlts ?? []).Where(a => a is not null).OrderBy(a => a, StringComparer.Ordinal));

		return $"link={HasLink} href={Href ?? "<null>"} title={Title ?? "<null>"} text={Text ?? "<null>"} alts={alts}";
	}
}

/// <summary>
/// Reads one line of a &lt;pre&gt;-style listing into a <see cref="LineView"/>.
///
/// Almost every line is the same simple shape (lowercase &lt;a&gt; and &lt;img&gt; with plain double-quoted
/// attributes and plain text), and those are read directly with no DOM at all. Anything else (entities, other
/// tags, odd quoting, ...) goes through AngleSharp and is read from its DOM, so the answer is the same either
/// way; LineFragmentParserTests compares both on every line of every sample listing.
/// </summary>
public static class LineFragmentParser
{
	private static readonly HtmlParser HtmlParser = new();

	// AngleSharp documents are not thread safe, and the crawler parses on many threads: one shell per thread
	[ThreadStatic]
	private static IHtmlDocument _shellDocument;

	private static IHtmlDocument ShellDocument => _shellDocument ??= HtmlParser.ParseDocument(string.Empty);

	public static LineView Parse(string line)
	{
		return TryParseSimple(line) ?? ReadDom(ParseWithAngleSharp(line));
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

	/// <summary>What the parsers would have asked of the DOM, answered from the DOM.</summary>
	public static LineView ReadDom(IElement container)
	{
		IElement link = container.QuerySelector("a");

		LineView view = new()
		{
			HasLink = link is not null,
			Href = link?.Attributes["href"]?.Value,
			Title = link is null ? null : (link as IHtmlAnchorElement)?.Title,
			Text = link?.TextContent
		};

		foreach (IElement image in container.QuerySelectorAll("img"))
		{
			view.AddImageAlt(image.Attributes["alt"]?.Value);
		}

		return view;
	}

	/// <summary>
	/// True when the line is simple AND its raw text is exactly what AngleSharp would serialize it back to (only
	/// the attributes listings use, no characters the serializer would escape). A page whose lines are all strict
	/// can be read from its raw text instead of from a DOM; see RawPreListing.
	/// </summary>
	public static bool IsStrictListingLine(string line) => TryParseSimple(line, strict: true) is not null;

	/// <summary>The same answers for the simple shape, or null when the line is anything else (the caller then uses AngleSharp).</summary>
	public static LineView TryParseSimple(string line, bool strict = false)
	{
		bool hasLink = false;
		string href = null;
		string title = null;
		string linkText = null;
		List<string> alts = null;
		int position = 0;

		while (position < line.Length)
		{
			int tagStart = line.IndexOf('<', position);
			int textEnd = tagStart < 0 ? line.Length : tagStart;

			if (textEnd > position && !IsPlainText(line, position, textEnd, strict))
			{
				return null;
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

			ReadOnlySpan<char> tagName = line.AsSpan(nameStart, position - nameStart);
			bool isAnchor = tagName.SequenceEqual("a");

			if (!isAnchor && !tagName.SequenceEqual("img"))
			{
				return null;
			}

			string elementHref = null;
			string elementTitle = null;
			string elementAlt = null;

			if (!TryReadAttributes(line, ref position, ref elementHref, ref elementTitle, ref elementAlt, strict))
			{
				return null;
			}

			if (isAnchor)
			{
				int closeStart = line.IndexOf('<', position);

				if (closeStart < 0 || string.CompareOrdinal(line, closeStart, "</a>", 0, 4) != 0 || !IsPlainText(line, position, closeStart, strict))
				{
					return null;
				}

				// QuerySelector("a") is the first anchor in the line; later ones only have to be simple too
				if (!hasLink)
				{
					hasLink = true;
					href = elementHref;
					title = elementTitle;
					linkText = line.Substring(position, closeStart - position);
				}

				position = closeStart + 4;
			}
			else if (elementAlt is not null)
			{
				(alts ??= []).Add(elementAlt);
			}
		}

		LineView view = new()
		{
			HasLink = hasLink,
			Href = href,
			Title = title,
			Text = linkText
		};

		if (alts is not null)
		{
			foreach (string alt in alts)
			{
				view.AddImageAlt(alt);
			}
		}

		return view;
	}

	/// <summary>Reads ` name="value"` pairs up to the closing '>', keeping href, title and alt. Anything not strictly that shape fails, leaving it to AngleSharp.</summary>
	private static bool TryReadAttributes(string line, ref int position, ref string href, ref string title, ref string alt, bool strict)
	{
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

			ReadOnlySpan<char> name = line.AsSpan(nameStart, position - nameStart);
			int valueStart = position + 2;
			int valueEnd = line.IndexOf('"', valueStart);

			if (valueEnd < 0 || !IsPlainText(line, valueStart, valueEnd, strict) || line.AsSpan(valueStart, valueEnd - valueStart).IndexOfAny('\'', '>') >= 0)
			{
				return false;
			}

			// Strict: only the attributes a listing line uses, so nothing in the markup can matter to a selector (id, class, ...)
			if (strict && !(name.SequenceEqual("href") || name.SequenceEqual("src") || name.SequenceEqual("alt") || name.SequenceEqual("title")))
			{
				return false;
			}

			// The HTML parser keeps the first of a duplicate attribute: don't try to be clever about those
			// (attributes nobody reads can repeat freely, they never reach the LineView)
			if (name.SequenceEqual("href"))
			{
				if (href is not null)
				{
					return false;
				}

				href = line.Substring(valueStart, valueEnd - valueStart);
			}
			else if (name.SequenceEqual("title"))
			{
				if (title is not null)
				{
					return false;
				}

				title = line.Substring(valueStart, valueEnd - valueStart);
			}
			else if (name.SequenceEqual("alt"))
			{
				if (alt is not null)
				{
					return false;
				}

				alt = line.Substring(valueStart, valueEnd - valueStart);
			}

			position = valueEnd + 1;

			// Attributes need whitespace between them; anything else is parse-error recovery territory
			if (position >= line.Length || (line[position] != ' ' && line[position] != '>'))
			{
				return false;
			}
		}
	}

	/// <summary>
	/// No entities (they would need decoding), no characters the HTML parser rewrites, no tag start. Strict also
	/// leaves out the two characters AngleSharp writes back differently: a bare '>' (as &amp;gt;) and a non-breaking space (as &amp;nbsp;).
	/// </summary>
	private static bool IsPlainText(string line, int start, int end, bool strict = false)
	{
		for (int i = start; i < end; i++)
		{
			char c = line[i];

			if (c is '&' or '<' or '\0' or '\r' or '\n' or '\f' or '\t' || char.IsSurrogate(c) || (strict && c is '>' or '\u00A0'))
			{
				return false;
			}
		}

		return true;
	}
}
