using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Listings.Pre;
using System.Net;

namespace OpenDirectoryDownloader.Parsing.Html;

/// <summary>Everything the listing steps know about the one page being parsed.</summary>
public sealed class ParseContext
{
	internal ParseContext(ParserServices services, WebDirectory webDirectory, string html, HttpResponseMessage response, bool checkParents, bool allowRawPre)
	{
		Services = services;
		WebDirectory = webDirectory;
		Html = html;
		Response = response;
		CheckParents = checkParents;
		AllowRawPre = allowRawPre;

		string baseUrl = webDirectory.Url;

		if (!baseUrl.EndsWith('/') && string.IsNullOrWhiteSpace(webDirectory.Uri.Query) && string.IsNullOrWhiteSpace(Path.GetExtension(baseUrl)))
		{
			baseUrl += '/';
		}

		BaseUrl = baseUrl;
		Host = webDirectory.Uri.Host.ToLower();

		Parsed = new WebDirectory(webDirectory.ParentDirectory)
		{
			Url = baseUrl,
			Name = WebUtility.UrlDecode(Path.GetDirectoryName(new Uri(baseUrl).Segments.LastOrDefault())) ?? Constants.Root
		};
	}

	public ParserServices Services { get; }

	public ILogger Logger => Services.Logger;

	/// <summary>The directory as it is in the scanned tree; the parsers build their result on <see cref="Parsed"/>.</summary>
	public WebDirectory WebDirectory { get; }

	public string Html { get; }

	/// <summary>The response the page came from, null when it did not come straight from the server (tests, the browser).</summary>
	public HttpResponseMessage Response { get; }

	public bool CheckParents { get; }

	/// <summary>False on the second attempt, after the raw &lt;pre&gt; shortcut found nothing.</summary>
	public bool AllowRawPre { get; }

	/// <summary>The directory URL, with a trailing slash when it looks like a directory without one.</summary>
	public string BaseUrl { get; }

	/// <summary>Host of the directory, lower case</summary>
	public string Host { get; }

	/// <summary>The result being built. Shared by all steps: a step that gives up can leave entries for the later ones.</summary>
	public WebDirectory Parsed { get; }

	/// <summary>Set by the BuildDom step</summary>
	public IHtmlDocument Document { get; internal set; }

	/// <summary>The big &lt;pre&gt; read from the raw HTML, when the shortcut applies to this page</summary>
	public RawPreListing.Extracted RawPre { get; internal set; }

	/// <summary>The link fallback already cleaned up <see cref="Parsed"/>.</summary>
	internal bool ResultChecked { get; set; }
}
