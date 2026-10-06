using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using OpenDirectoryDownloader.Parsing.Listings.Pre;
using OpenDirectoryDownloader.Urls;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Listings;

/// <summary>Small checks every listing parser shares.</summary>
internal static partial class ListingHelpers
{
	public static readonly char[] TrimChars = ['/'];

	public static void ProcessUrl(string baseUrl, IElement link, out string linkHref, out Uri uri, out string fullUrl)
	{
		UrlHelper.ProcessUrl(baseUrl, link.Attributes["href"]?.Value, out linkHref, out uri, out fullUrl);
	}

	/// <summary>Check simple cases of file size</summary>
	public static bool IsFileSize(string value)
	{
		return value != "-" && value != "—" && value != "<Directory>";
	}

	public static bool IsValidLink(IElement link)
	{
		if (link == null)
		{
			return false;
		}

		return IsValidLink(link.Attributes["href"]?.Value, (link as IHtmlAnchorElement)?.Title, link.TextContent);
	}

	public static bool IsValidLink(LineView line)
	{
		return line.HasLink && IsValidLink(line.Href, line.Title, line.Text);
	}

	public static bool IsValidLink(string linkHref, string title, string text)
	{
		return
			linkHref != "/" &&
			linkHref != ".." &&
			linkHref != "../" &&
			linkHref != "./." &&
			linkHref != "./.." &&
			linkHref != "#" &&
			title != ".." &&
			text.Trim() != ".." &&
			text.Trim() != "." &&
			linkHref?.StartsWith("javascript:", StringComparison.InvariantCultureIgnoreCase) == false &&
			linkHref?.StartsWith("mailto:", StringComparison.InvariantCultureIgnoreCase) == false &&
			!text.Equals("parent directory", StringComparison.InvariantCultureIgnoreCase) &&
			!text.Equals("[to parent directory]", StringComparison.InvariantCultureIgnoreCase) &&
			text.Trim() != "Name" &&
			linkHref?.Contains("&expand") == false &&
			(!RegexNMSDAD().IsMatch(linkHref) || linkHref.StartsWith("DirectoryList.asp")) &&
			(Path.GetFileName(linkHref) != "DirectoryList.asp" || !string.IsNullOrWhiteSpace(text));
	}

	[GeneratedRegex(@"\?[NMSD]=?[AD]")]
	private static partial Regex RegexNMSDAD();
}
