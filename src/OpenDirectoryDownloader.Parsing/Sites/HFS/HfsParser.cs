using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using System.Net;

namespace OpenDirectoryDownloader.Parsing.Sites.HFS;

public static class HfsParser
{
	public const string Parser = "HFS";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, string html, string httpHeaderServer)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(httpHeaderServer))
			{
				services.Session.SiteState.Get<HfsState>().ServerHeader = httpHeaderServer;
			}

			webDirectory = await ScanAsync(services, webDirectory, html);
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error parsing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			throw;
		}

		return webDirectory;
	}

	public static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory, string html)
	{
		services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Url);
		webDirectory.Parser = Parser;

		try
		{
			bool useGetFileList = true;

			if (services.Session.SiteState.Get<HfsState>().ServerHeader is string httpHeaderServerSession)
			{
				useGetFileList =
					httpHeaderServerSession.StartsWith("HFS 2023", StringComparison.InvariantCultureIgnoreCase) ||
					httpHeaderServerSession.StartsWith("HFS 0.", StringComparison.InvariantCultureIgnoreCase);
			}

			string url = useGetFileList ?
				$"{webDirectory.Uri.Scheme}://{webDirectory.Uri.Host}{(webDirectory.Uri.IsDefaultPort ? string.Empty : $":{webDirectory.Uri.Port}")}/~/api/get_file_list?csrf=&uri={WebUtility.UrlEncode(webDirectory.Uri.AbsolutePath)}&omit=c" :
				$"{webDirectory.Uri.Scheme}://{webDirectory.Uri.Host}{(webDirectory.Uri.IsDefaultPort ? string.Empty : $":{webDirectory.Uri.Port}")}/~/api/file_list?csrf=&path={WebUtility.UrlEncode(webDirectory.Uri.AbsolutePath)}&omit=c";

			HttpResponseMessage httpResponseMessage = await services.Http.GetAsync(url);
			string content = await httpResponseMessage.Content.ReadAsStringAsync();

			HfsListing hfsListing = HfsListing.FromJson(content);

			string baseUrl = $"{webDirectory.Uri.Scheme}://{webDirectory.Uri.Host}{(webDirectory.Uri.IsDefaultPort ? string.Empty : $":{webDirectory.Uri.Port}")}{webDirectory.Uri.AbsolutePath}";
			Uri baseUri = new(baseUrl);

			foreach (List hfsItem in hfsListing.List)
			{
				bool isDirectory = hfsItem.S is null || hfsItem.N.EndsWith('/');

				if (isDirectory)
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						Url = new Uri(baseUri, hfsItem.N).ToString(),
						Name = hfsItem.N
					});

					continue;
				}

				webDirectory.Files.Add(new WebFile
				{
					Url = new Uri(baseUri, hfsItem.N).ToString(),
					FileName = hfsItem.N,
					FileSize = hfsItem.S
				});
			}
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return await Task.FromResult(webDirectory);
	}
}

/// <summary>The HFS version, from the Server header of the first page</summary>
public sealed class HfsState
{
	public string ServerHeader { get; set; }
}
