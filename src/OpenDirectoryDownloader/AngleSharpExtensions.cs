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

	private static void AppendEscaped(StringBuilder builder, string value, bool attribute)
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
