using Microsoft.Extensions.Logging;
using Acornima;
using Acornima.Ast;
using OpenDirectoryDownloader.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Sites.Dropbox;

public static class DropboxParser
{
	private static readonly Regex UrlRegex = new(@"\/sh\/(?<LinkKey>[^\/]*)\/(?<SecureHash>[^\/?]*)(?:\/(?<SubPath>[^?]*))?");
	private static readonly Regex PrefetchListingRegex = new(@"window\[""__REGISTER_SHARED_LINK_FOLDER_PRELOAD_HANDLER""\]\.responseReceived\((?<PrefetchListing>"".*)\)\s?}\);");
	private const string Parser = "Dropbox";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, string html, HttpResponseMessage httpResponseMessage)
	{
		try
		{
			webDirectory = await ScanAsync(services, webDirectory, html, httpResponseMessage);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			throw;
		}

		return webDirectory;
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory, string html, HttpResponseMessage httpResponseMessage)
	{
		services.Logger.LogDebug("Retrieving listings for '{url}'", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			if (services.Http.DefaultRequestHeaders.UserAgent.Count == 0)
			{
				services.Http.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Chrome);
			}

			GetCsrfToken(services, webDirectory, httpResponseMessage);

			Match prefetchListingRegexMatch = PrefetchListingRegex.Match(html);

			if (prefetchListingRegexMatch.Success)
			{
				string htmlJavascriptString = prefetchListingRegexMatch.Groups["PrefetchListing"].Value;
				Parser javaScriptParser = new();
				Script program = javaScriptParser.ParseScript(htmlJavascriptString);
				string decodedJson = (program.Body[0].ChildNodes.First() as Literal).Raw;

				Match urlRegexMatch = UrlRegex.Match(webDirectory.Uri.ToString());
				bool takedownActive = false;

				DropboxResult dropboxResult = DropboxResult.FromJson(decodedJson);
				takedownActive = takedownActive || dropboxResult.TakedownRequestType is not null;

				List<Entry> entries = [.. dropboxResult.Entries];

				if (dropboxResult.HasMoreEntries)
				{
					do
					{
						Dictionary<string, string> postValues = new()
						{
							{ "is_xhr", "true" },
							{ "t", services.Session.SiteState.Get<DropboxState>().CsrfToken ?? string.Empty },
							{ "link_key", urlRegexMatch.Groups["LinkKey"].Value },
							{ "link_type", "s" },
							{ "secure_hash", urlRegexMatch.Groups["SecureHash"].Value },
							{ "sub_path", urlRegexMatch.Groups["SubPath"].Value },
							{ "voucher", dropboxResult.NextRequestVoucher }
						};

						HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, "https://www.dropbox.com/list_shared_link_folder_entries") { Content = new FormUrlEncodedContent(postValues) };
						httpResponseMessage = await services.Http.SendAsync(httpRequestMessage);

						httpResponseMessage.EnsureSuccessStatusCode();

						string response = await httpResponseMessage.Content.ReadAsStringAsync();

						dropboxResult = DropboxResult.FromJson(response);
						takedownActive |= dropboxResult.TakedownRequestType is not null;

						entries.AddRange(dropboxResult.Entries);
					} while (dropboxResult?.HasMoreEntries == true);
				}

				foreach (Entry entry in entries)
				{
					if (entry.IsDir || entry.IsSymlink)
					{
						webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
						{
							Parser = Parser,
							Url = entry.Href.ToString(),
							Name = entry.Filename
						});
					}
					else
					{
						webDirectory.Files.Add(new WebFile
						{
							Url = entry.Href?.ToString(),
							FileName = entry.Filename,
							FileSize = entry.Bytes
						});
					}
				}

				if (takedownActive)
				{
					services.Logger.LogWarning("Some entries are not provided because of DCMA/takedown.");
				}
			}
			else
			{
				throw new Exception("Cannot find prefetch listing");
			}
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for '{url}'", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return webDirectory;
	}

	private static void GetCsrfToken(ParserServices services, WebDirectory webDirectory, HttpResponseMessage httpResponseMessage)
	{
		if (httpResponseMessage.Headers.Contains("Set-Cookie"))
		{
			CookieContainer cookieContainer = new();

			foreach (string cookieHeader in httpResponseMessage.Headers.GetValues("Set-Cookie"))
			{
				cookieContainer.SetCookies(webDirectory.Uri, cookieHeader);
			}

			if (services.Session.SiteState.Get<DropboxState>().CsrfToken is null)
			{
				Cookie cookie = cookieContainer.GetCookies(webDirectory.Uri).FirstOrDefault(c => c.Name == "__Host-js_csrf");

				if (cookie is not null)
				{
					services.Logger.LogWarning($"CSRF Token found on {webDirectory.Uri}");
					services.Session.SiteState.Get<DropboxState>().CsrfToken = cookie.Value;
				}
			}
		}
	}
}

/// <summary>The CSRF token Dropbox hands out with the first page</summary>
public sealed class DropboxState
{
	public string CsrfToken { get; set; }
}
