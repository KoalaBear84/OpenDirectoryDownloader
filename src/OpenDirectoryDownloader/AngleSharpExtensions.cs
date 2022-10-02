using AngleSharp.Dom;

namespace OpenDirectoryDownloader;

public static class AngleSharpExtensions
{
	public static IElement Parent(this IElement element, string elementName)
	{
		IElement parentElement = element;

		do
		{
			parentElement = parentElement.ParentElement;
		} while (parentElement != null && parentElement.TagName.ToUpperInvariant() != elementName.ToUpperInvariant());

		return parentElement;
	}
}
