using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;

namespace OpenDirectoryDownloader.Parsing.Sites.CrushFtp;

public static class CrushFtpParser
{
	private const string Parser = "CrushFTP";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory)
	{
		CrushFtpState state = services.Session.SiteState.Get<CrushFtpState>();

		try
		{
			if (string.IsNullOrWhiteSpace(state.Authentication))
			{
				state.Authentication = services.Cookies?.GetAllCookies().FirstOrDefault(c => c.Name == "currentAuth")?.Value;
				state.FunctionUrl = $"{webDirectory.Uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port, UriFormat.UriEscaped)}/WebInterface/function/";
			}

			webDirectory = await ScanAsync(services, webDirectory);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error parsing {parser} for URL: {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
	{
		services.Logger.LogDebug("Retrieving listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		CrushFtpState state = services.Session.SiteState.Get<CrushFtpState>();

		try
		{
			Dictionary<string, string> postValues = new()
			{
				{ "command", "getXMLListing" },
				{ "format", "JSONOBJ" },
				{ "path", $"{Uri.EscapeDataString(GetListingPath(webDirectory.Uri))}%2F" },
				{ "random", Random.Shared.NextDouble().ToString() },
				{ "c2f", state.Authentication }
			};

			HttpResponseMessage httpResponseMessage = await services.Http.PostAsync(state.FunctionUrl, new FormUrlEncodedContent(postValues));

			httpResponseMessage.EnsureSuccessStatusCode();

			string response = await httpResponseMessage.Content.ReadAsStringAsync();

			CrushFtpResult crushFtpResult = CrushFtpResult.FromJson(response);

			foreach (Listing listing in crushFtpResult.Listing ?? [])
			{
				if (listing.Type == "DIR")
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = new Uri(webDirectory.Uri, EscapeHrefPath(listing.HrefPath)).ToString(),
						Name = listing.Name
					});
				}
				else
				{
					webDirectory.Files.Add(new WebFile
					{
						Url = new Uri(webDirectory.Uri, EscapeHrefPath(listing.HrefPath)).ToString(),
						FileName = listing.Name,
						FileSize = listing.Size
					});
				}
			}
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for URL: {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			throw;
		}

		return webDirectory;
	}

	/// <summary>The characters in a name that would otherwise be read as a query, a fragment, an escape sequence or a path separator</summary>
	private static string EscapeHrefPath(string hrefPath) => hrefPath.Replace("%", "%25").Replace("#", "%23").Replace("?", "%3F").Replace("\\", "%5C");

	/// <summary>The login page of the web interface, for a server that hides its Server header (behind Cloudflare or another proxy)</summary>
	public static bool IsWebInterface(string html) =>
		html is not null && html.Contains("/WebInterface/", StringComparison.Ordinal) && html.Contains("CrushFTP", StringComparison.OrdinalIgnoreCase);

	/// <summary>The directory to list: the web interface keeps it in the fragment (https://host/#/dir/subdir/), the real path of the URL otherwise</summary>
	internal static string GetListingPath(Uri uri) => uri.Fragment.StartsWith("#/", StringComparison.Ordinal) ? Uri.UnescapeDataString(uri.Fragment[1..]) : uri.LocalPath;
}

/// <summary>The session of the CrushFTP web interface, from its login cookie</summary>
public sealed class CrushFtpState
{
	public string Authentication { get; set; }

	public string FunctionUrl { get; set; }
}
