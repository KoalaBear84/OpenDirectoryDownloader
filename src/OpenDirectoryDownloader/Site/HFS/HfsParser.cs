using OpenDirectoryDownloader.Shared.Models;
using System.Net;

namespace OpenDirectoryDownloader.Site.HFS;

public static class HfsParser
{
	public const string Parser = "HFS";

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, string html, string httpHeaderServer)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(httpHeaderServer))
			{
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_HttpHeader_Server] = httpHeaderServer;
			}

			webDirectory = await ScanAsync(httpClient, webDirectory, html);
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error parsing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			throw;
		}

		return webDirectory;
	}

	public static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory, string html)
	{
		Program.Logger.Debug("Retrieving listings for {url}", webDirectory.Url);
		webDirectory.Parser = Parser;

		try
		{
			bool useGetFileList = true;

			if (OpenDirectoryIndexer.Session.Parameters.TryGetValue(Constants.Parameters_HttpHeader_Server, out string httpHeaderServerSession))
			{
				useGetFileList =
					httpHeaderServerSession.StartsWith("HFS 2023", StringComparison.InvariantCultureIgnoreCase) ||
					httpHeaderServerSession.StartsWith("HFS 0.", StringComparison.InvariantCultureIgnoreCase);
			}

			string url = useGetFileList ?
				$"{webDirectory.Uri.Scheme}://{webDirectory.Uri.Host}{(webDirectory.Uri.IsDefaultPort ? string.Empty : $":{webDirectory.Uri.Port}")}/~/api/get_file_list?csrf=&uri={WebUtility.UrlEncode(webDirectory.Uri.AbsolutePath)}&omit=c" :
				$"{webDirectory.Uri.Scheme}://{webDirectory.Uri.Host}{(webDirectory.Uri.IsDefaultPort ? string.Empty : $":{webDirectory.Uri.Port}")}/~/api/file_list?csrf=&path={WebUtility.UrlEncode(webDirectory.Uri.AbsolutePath)}&omit=c";

			HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url);
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
			Program.Logger.Error(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			//throw;
		}

		return await Task.FromResult(webDirectory);
	}
}
