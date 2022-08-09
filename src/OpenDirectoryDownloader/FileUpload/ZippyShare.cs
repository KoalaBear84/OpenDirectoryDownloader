using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using NLog;
using OpenDirectoryDownloader.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.FileUpload;

public class ZippyShare : IFileUploadSite
{
	public string Name => "Zippyshare.com";

	private readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private static readonly HtmlParser HtmlParser = new();

	private static Regex UploadIdRegex = new(@"var uploadId = '(?<UploadId>[A-Z0-9]*)';");
	private static Regex ServerRegex = new(@"var server = '(?<Server>www\d*)';");

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

				using (MultipartFormDataContent multipartFormDataContent = new($"Upload----{Guid.NewGuid()}"))
				{
					multipartFormDataContent.Add(new StringContent("on"), "terms");
					multipartFormDataContent.Add(new StringContent("on"), "private");
					multipartFormDataContent.Add(new StringContent(uploadId), "uploadId");
					multipartFormDataContent.Add(new StreamContent(new FileStream(path, FileMode.Open)), "file", Path.GetFileName(path));

					using (HttpResponseMessage httpResponseMessage = await httpClient.PostAsync($"https://{server}.zippyshare.com/upload", multipartFormDataContent))
					{
						if (httpResponseMessage.IsSuccessStatusCode)
						{
							string response = await httpResponseMessage.Content.ReadAsStringAsync();
							OpenDirectoryIndexer.Session.UploadedUrlsResponse = response;

							Logger.Debug($"Response from {Name}: {response}");

							IHtmlDocument htmlDocument = await HtmlParser.ParseDocumentAsync(response);
							IHtmlAnchorElement link = htmlDocument.QuerySelector<IHtmlAnchorElement>("#urls a");

							if (link == null)
							{
								throw new Exception($"{Name} error, cannot find link");
							}

							return new ZippyShareFile
							{
								Url = link.Href
							};
						}
						else
						{
							Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
							await Task.Delay(TimeSpan.FromSeconds(5));
						}
					}
				}

				retries++;
			}
			catch (Exception)
			{
				retries++;
				Logger.Error($"Error uploading file... Retry in 5 seconds!!!");
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
