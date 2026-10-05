using AngleSharp;
using AngleSharp.Dom;
using System.Text;

namespace OpenDirectoryDownloader;

public static class AngleSharpExtensions
{
	public static IElement Parent(this IElement element, string elementName)
	{
		IElement parentElement = element;

		do
		{
			parentElement = parentElement.ParentElement;
		} while (parentElement != null && !parentElement.TagName.Equals(elementName, StringComparison.InvariantCultureIgnoreCase));

		return parentElement;
	}

	private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";

	private static readonly HashSet<string> VoidElements = ["area", "base", "basefont", "bgsound", "br", "col", "embed", "frame", "hr", "img", "input", "keygen", "link", "meta", "param", "source", "track", "wbr"];

	// Elements whose text content is written without escaping
	private static readonly HashSet<string> RawTextElements = ["script", "style", "xmp", "iframe", "noembed", "noframes", "plaintext", "noscript"];

	// Elements left to AngleSharp when they are a child: raw text, and the ones the HTML serializer treats specially
	private static readonly HashSet<string> SerializedByAngleSharp = [.. RawTextElements, "template", "pre", "textarea", "listing"];

	/// <summary>
	/// The same string as <c>element.InnerHtml</c>, without its cost: AngleSharp serializes each node in time
	/// that grows with the number of siblings, so a &lt;pre&gt; with tens of thousands of links (a big Apache or
	/// nginx listing) took minutes (80,000 files: 38 seconds, versus under a second to parse the page).
	/// Plain text and ordinary HTML elements are written here with the same escaping rules as AngleSharp's
	/// HtmlMarkupFormatter; anything unusual (other namespaces, raw-text elements, comments, ...) is still
	/// handed to AngleSharp, so the result stays identical.
	/// </summary>
	public static string FastInnerHtml(this IElement element)
	{
		StringBuilder builder = new();

		foreach (INode child in element.ChildNodes)
		{
			AppendNode(builder, child);
		}

		return builder.ToString();
	}

	/// <summary>
	/// The same lines as <c>RegexPreLineSeparator().Split(element.FastInnerHtml())</c>, without first building the whole
	/// content as one string: for a big listing that string, plus the copy it takes to split it, was about 900 bytes
	/// per file. Text is split at line breaks and a bare &lt;hr&gt; ends a line, exactly like the regex does; elements
	/// are serialized one at a time and added to the current line.
	/// Returns null when something unusual makes the exact result depend on the regex (a carriage return, a
	/// &lt;br&gt;, a line break or &lt;hr&gt; inside an element, a very large element); the caller then uses the
	/// string-and-regex way.
	/// </summary>
	public static List<string> FastPreLines(this IElement element)
	{
		List<string> lines = [];
		StringBuilder line = new();
		// Big enough that one element's markup stays in a single chunk, which is what lets the checks below look at it chunk by chunk
		StringBuilder piece = new(MaxPieceLength + 64);

		foreach (INode child in element.ChildNodes)
		{
			if (child is IText text)
			{
				string data = text.Data;
				int start = 0;

				for (int i = 0; i < data.Length; i++)
				{
					if (data[i] == '\r')
					{
						return null;
					}

					if (data[i] == '\n')
					{
						AppendEscaped(line, data.AsSpan(start, i - start), attribute: false);
						lines.Add(line.ToString());
						line.Clear();
						start = i + 1;
					}
				}

				AppendEscaped(line, data.AsSpan(start), attribute: false);
				continue;
			}

			if (child is IElement { LocalName: "hr", NamespaceUri: HtmlNamespace, Attributes.Length: 0 })
			{
				lines.Add(line.ToString());
				line.Clear();
				continue;
			}

			piece.Clear();
			AppendNode(piece, child);

			if (piece.Length > MaxPieceLength || !IsSafePiece(piece))
			{
				return null;
			}

			line.Append(piece);
		}

		lines.Add(line.ToString());

		return lines;
	}

	private const int MaxPieceLength = 4096;

	/// <summary>No line break and nothing the line-separator regex could match inside one element's markup.</summary>
	private static bool IsSafePiece(StringBuilder piece)
	{
		foreach (ReadOnlyMemory<char> chunk in piece.GetChunks())
		{
			ReadOnlySpan<char> span = chunk.Span;

			if (span.IndexOfAny('\r', '\n') >= 0 || span.IndexOf("<br", StringComparison.Ordinal) >= 0 || span.IndexOf("<hr>", StringComparison.Ordinal) >= 0)
			{
				return false;
			}
		}

		return true;
	}

	private static void AppendNode(StringBuilder builder, INode node)
	{
		switch (node)
		{
			case IText text when node.ParentElement is IElement parent && !RawTextElements.Contains(parent.LocalName):
				AppendEscaped(builder, text.Data, attribute: false);
				break;
			case IElement child when child.NamespaceUri == HtmlNamespace && !SerializedByAngleSharp.Contains(child.LocalName) && child.Attributes.All(a => a.NamespaceUri is null):
				builder.Append('<').Append(child.LocalName);

				foreach (IAttr attribute in child.Attributes)
				{
					builder.Append(' ').Append(attribute.Name).Append("=\"");
					AppendEscaped(builder, attribute.Value, attribute: true);
					builder.Append('"');
				}

				builder.Append('>');

				if (VoidElements.Contains(child.LocalName))
				{
					break;
				}

				foreach (INode grandChild in child.ChildNodes)
				{
					AppendNode(builder, grandChild);
				}

				builder.Append("</").Append(child.LocalName).Append('>');
				break;
			default:
				builder.Append(node.ToHtml());
				break;
		}
	}

	private static void AppendEscaped(StringBuilder builder, ReadOnlySpan<char> value, bool attribute)
	{
		foreach (char c in value)
		{
			switch (c)
			{
				case '&':
					builder.Append("&amp;");
					break;
				case ' ':
					builder.Append("&nbsp;");
					break;
				case '<' when !attribute:
					builder.Append("&lt;");
					break;
				case '>' when !attribute:
					builder.Append("&gt;");
					break;
				case '"' when attribute:
					builder.Append("&quot;");
					break;
				default:
					builder.Append(c);
					break;
			}
		}
	}
}
