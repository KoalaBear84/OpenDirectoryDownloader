using AngleSharp.Html.Dom;
using Jint;
using Jint.Native;
using NLog;
using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GoIndex.Bhadoo;

/// <summary>
/// Similar to GoIndex
/// </summary>
public static class BhadooIndexParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	const string Parser = "BhadooIndex";
	static readonly RateLimiter RateLimiter = new RateLimiter(1, TimeSpan.FromSeconds(1));
	static Engine JintEngine { get; set; }

	public static async Task<WebDirectory> ParseIndex(IHtmlDocument htmlDocument, HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
			{
				Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				Logger.Info($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");

				Console.WriteLine("Check if password is needed (unsupported currently)...");
				Logger.Info("Check if password is needed (unsupported currently)...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = "@ttu001";

				Dictionary<string, string> postValues = new Dictionary<string, string>
					{
						{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
						{ "page_token", string.Empty },
						{ "page_index", "0" },
						{ "q", "" }
					};
				HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
				HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await DecodeResponse(htmlDocument, httpClient, httpResponseMessage);

					BhadooIndexResponse response = BhadooIndexResponse.FromJson(responseJson);

					webDirectory = await ScanAsync(htmlDocument, httpClient, webDirectory);
				}
			}
			else
			{
				webDirectory = await ScanAsync(htmlDocument, httpClient, webDirectory);
			}
		}
		catch (Exception ex)
		{
			RateLimiter.AddDelay(TimeSpan.FromSeconds(5));
			Logger.Error(ex, $"Error parsing {Parser} for URL: {webDirectory.Url}");
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

	private static async Task<string> DecodeResponse(IHtmlDocument htmlDocument, HttpClient httpClient, HttpResponseMessage httpResponseMessage)
	{
		string responseString = await httpResponseMessage.Content.ReadAsStringAsync();
		string responseJson = string.Empty;

		if (responseString.StartsWith("{"))
		{
			responseJson = responseString;
		}
		else
		{
			if (JintEngine == null)
			{
				IHtmlScriptElement appJsScript = htmlDocument.Scripts.FirstOrDefault(s => s.Source?.Contains("app.js") == true || s.Source?.Contains("app.min.js") == true);
				string appJsSource = await httpClient.GetStringAsync(appJsScript.Source);
				List<JavaScriptHelper.Function> functions = JavaScriptHelper.Parse(appJsSource);
				JavaScriptHelper.Function readFunction = functions.FirstOrDefault(f => f.Name == "read");

				JintEngine = new Engine();
				JintEngine.Execute(readFunction.Body);
			}

			JsValue jsValue = JintEngine.Invoke("read", responseString);
			responseJson = Encoding.UTF8.GetString(Convert.FromBase64String(jsValue.ToString()));
		}

		return responseJson;
	}

	private static async Task<WebDirectory> ScanAsync(IHtmlDocument htmlDocument, HttpClient httpClient, WebDirectory webDirectory)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri} with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}");

		webDirectory.Parser = Parser;

		try
		{
			await RateLimiter.RateLimit();

			if (!webDirectory.Url.EndsWith("/"))
			{
				webDirectory.Url += "/";
			}

			long pageIndex = 0;
			string nextPageToken = string.Empty;
			int errors = 0;

			do
			{
				Logger.Warn($"Retrieving listings for {webDirectory.Uri}, page {pageIndex + 1}");

				Dictionary<string, string> postValues = new Dictionary<string, string>
					{
						{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
						{ "page_token", nextPageToken },
						{ "page_index", pageIndex.ToString() }
					};
				HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
				HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);

				if (!httpResponseMessage.IsSuccessStatusCode)
				{
					Logger.Warn("Directory listing retrieval error (HTTP Error), waiting 10 seconds..");
					errors++;
					await Task.Delay(TimeSpan.FromSeconds(10));
					await RateLimiter.RateLimit();
				}
				else
				{
					webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
					httpResponseMessage.EnsureSuccessStatusCode();

					string responseJson = await DecodeResponse(htmlDocument, httpClient, httpResponseMessage);

					BhadooIndexResponse indexResponse = BhadooIndexResponse.FromJson(responseJson);

					webDirectory.ParsedSuccessfully = indexResponse.Data.Error == null;

					if (indexResponse.Data.Error?.Message == "Rate Limit Exceeded")
					{
						Logger.Warn("Rate limit exceeded, waiting 10 seconds..");
						errors++;
						await Task.Delay(TimeSpan.FromSeconds(10));
					}
					else
					{
						if (indexResponse.Data.Files == null)
						{
							Logger.Warn("Directory listing retrieval error (Files null), waiting 10 seconds..");
							errors++;
							await Task.Delay(TimeSpan.FromSeconds(10));
						}
						else
						{
							errors = 0;
							nextPageToken = indexResponse.NextPageToken;
							pageIndex = indexResponse.CurPageIndex + 1;

							foreach (File file in indexResponse.Data.Files)
							{
								if (file.MimeType == FolderMimeType)
								{
									webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
									{
										Parser = Parser,
										// Yes, string concatenation, do not use new Uri(webDirectory.Uri, file.Name), because things could end with a space...
										Url = $"{webDirectory.Uri}{file.Name}/",
										Name = file.Name
									});
								}
								else
								{
									webDirectory.Files.Add(new WebFile
									{
										Url = new Uri(webDirectory.Uri, file.Name).ToString(),
										FileName = file.Name,
										FileSize = file.Size
									});
								}
							}
						}
					}
				}

				if (errors >= 5)
				{
					throw new FriendlyException($"Error retrieving directory listing for {webDirectory.Uri}");
				}
			} while (!string.IsNullOrWhiteSpace(nextPageToken) || errors > 0);
		}
		catch (Exception ex)
		{
			RateLimiter.AddDelay(TimeSpan.FromSeconds(5));
			Logger.Error(ex, $"Error processing {Parser} for URL: {webDirectory.Url}");
			webDirectory.Error = true;

			OpenDirectoryIndexer.Session.Errors++;

			if (!OpenDirectoryIndexer.Session.UrlsWithErrors.Contains(webDirectory.Url))
			{
				OpenDirectoryIndexer.Session.UrlsWithErrors.Add(webDirectory.Url);
			}

			//throw;
		}

		return webDirectory;
	}
}
