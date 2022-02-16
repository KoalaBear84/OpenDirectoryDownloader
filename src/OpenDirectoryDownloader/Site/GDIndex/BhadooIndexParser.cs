using AngleSharp.Html.Dom;
using Esprima;
using Esprima.Ast;
using Jint;
using Jint.Native;
using NLog;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GDIndex.Bhadoo;

/// <summary>
/// Similar to GoIndex
/// </summary>
public static class BhadooIndexParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "BhadooIndex";
	private static readonly RateLimiter RateLimiter = new RateLimiter(1, TimeSpan.FromSeconds(1));
	private static readonly object DecodeResponseLock = new object();
	private static Engine JintEngine { get; set; }
	private static bool Obfuscated { get; set; }

	public static async Task<WebDirectory> ParseIndex(IHtmlDocument htmlDocument, HttpClient httpClient, WebDirectory webDirectory)
	{
		try
		{
			OpenDirectoryIndexer.Session.GDIndex = true;

			if (!OpenDirectoryIndexer.Session.Parameters.ContainsKey(Constants.Parameters_Password))
			{
				Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				Logger.Info($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");

				Console.WriteLine("Check if password is needed (unsupported currently)...");
				Logger.Info("Check if password is needed (unsupported currently)...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = string.Empty;

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

					if (response.Error != null)
					{
						string errorMessage = $"Error {response.Error.Code}, '{response.Error.Message}' retrieving for URL: {webDirectory.Url}";
						Logger.Error(errorMessage);
						throw new Exception(errorMessage);
					}

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
			lock (DecodeResponseLock)
			{
				if (JintEngine == null)
				{
					IHtmlScriptElement appJsScript = htmlDocument.Scripts.FirstOrDefault(s =>
						s.Source?.Contains("app.js") == true ||
						s.Source?.Contains("app.min.js") == true ||
						s.Source?.Contains("app.obf.js") == true ||
						s.Source?.Contains("app.obf.min.js") == true
					);

					Obfuscated = appJsScript.Source.Contains("obf.");

					string appJsSource = httpClient.GetStringAsync(appJsScript.Source.Replace("obf.", string.Empty)).GetAwaiter().GetResult();

					JavaScriptParser javaScriptParser = new JavaScriptParser(appJsSource);
					Script program = javaScriptParser.ParseScript();
					IEnumerable<FunctionDeclaration> javaScriptFunctions = program.ChildNodes.OfType<FunctionDeclaration>();
					FunctionDeclaration readFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "read"));
					string readFunction = appJsSource.Substring(readFunctionDeclaration.Range.Start, readFunctionDeclaration.Range.End - readFunctionDeclaration.Range.Start);

					JintEngine = new Engine();

					JintEngine.Execute(readFunction);

					if (Obfuscated)
					{
						Func<string, string> atob = str => Encoding.Latin1.GetString(Convert.FromBase64String(str));
						JintEngine.SetValue("atob", atob);

						FunctionDeclaration gdidecodeFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "gdidecode"));
						string gdidecodeFunction = appJsSource.Substring(gdidecodeFunctionDeclaration.Range.Start, gdidecodeFunctionDeclaration.Range.End - gdidecodeFunctionDeclaration.Range.Start);
						JintEngine.Execute(gdidecodeFunction);
					}
				}

				JsValue jsValue = JintEngine.Invoke("read", responseString);

				if (Obfuscated)
				{
					jsValue = JintEngine.Invoke("gdidecode", jsValue.ToString());
					responseJson = jsValue.ToString();
				}
				else
				{
					responseJson = Encoding.UTF8.GetString(Convert.FromBase64String(jsValue.ToString()));
				}
			}
		}

		return responseJson;
	}

	private static async Task<WebDirectory> ScanAsync(IHtmlDocument htmlDocument, HttpClient httpClient, WebDirectory webDirectory)
	{
		webDirectory.Parser = Parser;

		try
		{
			Polly.Retry.AsyncRetryPolicy asyncRetryPolicy = Library.GetAsyncRetryPolicy((ex, waitTimeSpan, retry, pollyContext) =>
			{
				Logger.Warn($"Error retrieving directory listing for {webDirectory.Uri}, waiting {waitTimeSpan.TotalSeconds} seconds.. Error: {ex.Message}");
				RateLimiter.AddDelay(waitTimeSpan);
			}, 8);

			if (!webDirectory.Url.EndsWith("/"))
			{
				webDirectory.Url += "/";
			}

			long pageIndex = 0;
			string nextPageToken = string.Empty;

			do
			{
				await asyncRetryPolicy.ExecuteAndCaptureAsync(async () =>
				{
					await RateLimiter.RateLimit();

					Logger.Warn($"Retrieving listings for {webDirectory.Uri.PathAndQuery}, page {pageIndex + 1}{(!string.IsNullOrWhiteSpace(OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]) ? $" with password: {OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]}" : string.Empty)}");

					Dictionary<string, string> postValues = new Dictionary<string, string>
					{
						{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
						{ "page_token", nextPageToken },
						{ "page_index", pageIndex.ToString() }
					};

					HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
					HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);

					webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;
					httpResponseMessage.EnsureSuccessStatusCode();

					try
					{
						string responseJson = await DecodeResponse(htmlDocument, httpClient, httpResponseMessage);

						BhadooIndexResponse indexResponse = BhadooIndexResponse.FromJson(responseJson);

						webDirectory.ParsedSuccessfully = indexResponse.Data?.Error == null && indexResponse.Error == null;

						if (indexResponse.Data?.Error?.Message == "Rate Limit Exceeded")
						{
							throw new Exception("Rate limit exceeded");
						}
						else if (indexResponse.Error != null)
						{
							webDirectory.Error = true;
							string errorMessage = $"Error {indexResponse.Error.Code}, '{indexResponse.Error.Message}' retrieving for URL: {webDirectory.Url}";
							Logger.Error(errorMessage);
							throw new Exception(errorMessage);
						}
						else
						{
							if (indexResponse.Data.Files == null)
							{
								throw new Exception("Directory listing retrieval error (Files null)");
							}
							else
							{
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
											Url = $"{webDirectory.Uri}{GetSafeName(file.Name)}/",
											Name = file.Name
										});
									}
									else
									{
										webDirectory.Files.Add(new WebFile
										{
											Url = new Uri(webDirectory.Uri, GetSafeName(file.Name)).ToString(),
											FileName = file.Name,
											FileSize = file.Size
										});
									}
								}
							}
						}
					}
					catch (Exception ex)
					{
						throw new Exception($"Retrieving listings for {webDirectory.Uri.PathAndQuery}, page {pageIndex + 1}. Error: {ex.Message}");
					}
				});
			} while (!string.IsNullOrWhiteSpace(nextPageToken));
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"Error retrieving directory listing for {webDirectory.Url}");
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

	private static string GetSafeName(string name)
	{
		return name
			.Replace("#", "%23")
			.Replace("/", "%2F");
	}
}
