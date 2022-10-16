using AngleSharp.Html.Dom;
using Esprima;
using Esprima.Ast;
using Jint;
using Jint.Native;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System.Text;

namespace OpenDirectoryDownloader.Site.GDIndex.Bhadoo;

/// <summary>
/// Similar to GoIndex
/// </summary>
public static class BhadooIndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "BhadooIndex";
	private static readonly RateLimiter RateLimiter = new(1, TimeSpan.FromSeconds(1));
	private static readonly object DecodeResponseLock = new();
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
				Program.Logger.Information("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				Console.WriteLine("Check if password is needed (unsupported currently)...");
				Program.Logger.Information("Check if password is needed (unsupported currently)...");
				OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] = string.Empty;

				Dictionary<string, string> postValues = new()
				{
					{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
					{ "page_token", string.Empty },
					{ "page_index", "0" },
					{ "q", "" }
				};

				HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
				HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await DecodeResponse(htmlDocument, httpClient, httpResponseMessage);

					BhadooIndexResponse response = BhadooIndexResponse.FromJson(responseJson);

					if (response.Error != null)
					{
						Program.Logger.Error("Error {errorCode}, '{errorMessage}' retrieving for '{url}'", response.Error.Code, response.Error.Message, webDirectory.Url);
						throw new Exception($"Error {response.Error.Code}, '{response.Error.Message}' retrieving for URL: {webDirectory.Url}");
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
			Program.Logger.Error(ex, "Error parsing {parser} for '{url}'", Parser, webDirectory.Url);
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
						s.Source?.Contains("app-multiple-drives.js") == true ||
						s.Source?.Contains("app-multiple-drives.min.js") == true ||
						s.Source?.Contains("app-multiple-drives.obf.js") == true ||
						s.Source?.Contains("app-read-only.js") == true ||
						s.Source?.Contains("app-read-only.min.js") == true ||
						s.Source?.Contains("app-read-only.obf.js") == true ||
						s.Source?.Contains("app-super-api.js") == true ||
						s.Source?.Contains("app-super-api.min.js") == true ||
						s.Source?.Contains("app-super-api.obf.js") == true ||
						s.Source?.Contains("app.min.js") == true ||
						s.Source?.Contains("app.obf.js") == true ||
						s.Source?.Contains("app.obf.min.js") == true
					);

					Obfuscated = appJsScript.Source.Contains("obf.");

					string appJsSource = httpClient.GetStringAsync(appJsScript.Source.Replace("obf.", string.Empty)).GetAwaiter().GetResult();

					JavaScriptParser javaScriptParser = new();
					Script program = javaScriptParser.ParseScript(appJsSource);
					IEnumerable<FunctionDeclaration> javaScriptFunctions = program.ChildNodes.OfType<FunctionDeclaration>();
					FunctionDeclaration readFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "read"));
					string readFunction = appJsSource[readFunctionDeclaration.Range.Start..readFunctionDeclaration.Range.End];

					JintEngine = new Engine();

					JintEngine.Execute(readFunction);

					if (Obfuscated)
					{
						Func<string, string> atob = str => Encoding.Latin1.GetString(Convert.FromBase64String(str));
						JintEngine.SetValue("atob", atob);

						FunctionDeclaration gdidecodeFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "gdidecode"));
						string gdidecodeFunction = appJsSource[gdidecodeFunctionDeclaration.Range.Start..gdidecodeFunctionDeclaration.Range.End];
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
				Program.Logger.Warning("Error retrieving directory listing for '{url}', waiting {waitTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
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

					Program.Logger.Warning("Retrieving listings for {relativeUrl}, page {page} with password: {password}", webDirectory.Uri.PathAndQuery, pageIndex + 1, OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password]);

					Dictionary<string, string> postValues = new()
					{
						{ "password", OpenDirectoryIndexer.Session.Parameters[Constants.Parameters_Password] },
						{ "page_token", nextPageToken },
						{ "page_index", pageIndex.ToString() }
					};

					HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
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
							Program.Logger.Error("Error {errprCode}, '{errorMessage}' retrieving for '{url}'", indexResponse.Error.Code, indexResponse.Error.Message, webDirectory.Url);
							throw new Exception($"Error {indexResponse.Error.Code}, '{indexResponse.Error.Message}' retrieving for URL: {webDirectory.Url}");
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
			Program.Logger.Error(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
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
