using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Scanning;
using System.Net;

namespace OpenDirectoryDownloader.Parsing;

/// <summary>
/// What the parsers may use while parsing the directories of one scan. Parsers are stateless functions; anything
/// they need to remember between directories lives in <see cref="ScanSession.SiteState"/>.
/// </summary>
public sealed class ParserServices
{
	public required ScanSession Session { get; init; }

	/// <summary>Null when parsing offline (tests): parsers that need extra requests are skipped then.</summary>
	public HttpClient Http { get; init; }

	public CookieContainer Cookies { get; init; }

	public ILogger Logger { get; init; } = NullLogger.Instance;

	public IUserConsole Console { get; init; } = NullUserConsole.Instance;

	/// <summary>Renders a page in a real browser, for pages that only work with JavaScript. Null when not available.</summary>
	public IBrowserRenderer Browser { get; init; }

	public ParserOptions Options { get; init; } = new();

	/// <summary>Services for parsing without a network, a browser or a console, as the tests do.</summary>
	public static ParserServices Offline(ScanSession session = null) => new()
	{
		Session = session ?? new ScanSession(new WebDirectory(null) { Url = "http://localhost/", Name = Constants.Root })
	};
}

public sealed record ParserOptions
{
	/// <summary>--no-browser: never start a browser</summary>
	public bool NoBrowser { get; init; }

	public string GitHubToken { get; init; }
}

/// <summary>Gets a page's HTML through a real browser and copies the browser's cookies to the crawler.</summary>
public interface IBrowserRenderer
{
	Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken = default);
}
