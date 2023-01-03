using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using OpenDirectoryDownloader.Models;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.FileUpload;

public class ZippyShare : IFileUploadSite
{
	public string Name => "Zippyshare.com";

	private static readonly HtmlParser HtmlParser = new();

	private static readonly Regex UploadIdRegex = new(@"var uploadId = '(?<UploadId>[A-Z0-9]*)';");
	private static readonly Regex ServerRegex = new(@"var server = '(?<Server>www\d*)';");

	public async Task<IFileUploadSiteFile> UploadFile(HttpClient httpClient, string path)
	{
		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries)
		{
			try
			{
				string html = await httpClient.GetStringAsync("https://www.zippyshare.com/sites/index_old.jsp");

				Match uploadIdMatch = UploadIdRegex.Match(html);
				Match serverMatch = ServerRegex.Match(html);

				if (!uploadIdMatch.Success || !serverMatch.Success)
				{
					throw new Exception($"{Name} error, cannot determine upload ID and/or server");
				}

				string uploadId = uploadIdMatch.Groups["UploadId"].Value;
				string server = serverMatch.Groups["Server"].Value;

				using MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}")
				{
					{ new StringContent("on"), "terms" },
					{ new StringContent("on"), "private" },
					{ new StringContent(uploadId), "uploadId" },
					{ new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path) }
				};

				using HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"https://{server}.zippyshare.com/upload", multipartFormDataContent);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string response = await httpResponseMessage.Content.ReadAsStringAsync();
					OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

					Program.Logger.Debug("Response from {siteName}: {response}", Name, response);

					IHtmlDocument htmlDocument = await HtmlParser.ParseDocumentAsync(response);
					IHtmlAnchorElement link = htmlDocument.QuerySelector<IHtmlAnchorElement>("#urls a") ?? throw new Exception($"{Name} error, cannot find link");

					return new ZippyShareFile
					{
						Url = link.Href
					};
				}
				else
				{
					Program.Logger.Error("Error uploading file, retry in 5 seconds..");
					await Task.Delay(TimeSpan.FromSeconds(5));
				}

				retries++;
			}
			catch (Exception ex)
			{
				retries++;
				Program.Logger.Error(ex, "Error uploading file, retry in 5 seconds..");
				await Task.Delay(TimeSpan.FromSeconds(5));
			}
		}

		throw new FriendlyException("Error uploading URLs");
	}
}

public class ZippyShareFile : IFileUploadSiteFile
{
	public string Url { get; set; }
}
