using Microsoft.Extensions.Logging;
using AngleSharp.Html.Dom;
using Acornima;
using Acornima.Ast;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Parsing.Sites;
using System.Text;

namespace OpenDirectoryDownloader.Parsing.Sites.GDIndex.Bhadoo;

/// <summary>
/// Similar to GoIndex
/// </summary>
public static class BhadooIndexParser
{
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string Parser = "BhadooIndex";

	public static async Task<WebDirectory> ParseIndex(IHtmlDocument htmlDocument, ParserServices services, WebDirectory webDirectory)
	{
		try
		{
			services.Session.SiteState.Get<GoogleDriveIndexState>().IsGoogleDriveIndex = true;

			if (!services.Session.SiteState.Get<IndexPasswordState>().IsKnown)
			{
				services.Console.WriteLine($"{Parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.");
				services.Logger.LogInformation("{parser} will always be indexed at a maximum rate of 1 per second, else you will run into problems and errors.", Parser);

				services.Console.WriteLine("Check if password is needed (unsupported currently)...");
				services.Logger.LogInformation("Check if password is needed (unsupported currently)...");
				services.Session.SiteState.Get<IndexPasswordState>().Set(string.Empty);

				Dictionary<string, string> postValues = new()
				{
					{ "password", services.Session.SiteState.Get<IndexPasswordState>().Password },
					{ "page_token", string.Empty },
					{ "page_index", "0" },
					{ "q", "" }
				};

				HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
				HttpResponseMessage httpResponseMessage = await services.Http.SendAsync(httpRequestMessage);

				if (httpResponseMessage.IsSuccessStatusCode)
				{
					string responseJson = await DecodeResponse(htmlDocument, services, httpResponseMessage);

					BhadooIndexResponse response = BhadooIndexResponse.FromJson(responseJson);

					if (response.Error != null)
					{
						services.Logger.LogError("Error {errorCode}, '{errorMessage}' retrieving for '{url}'", response.Error.Code, response.Error.Message, webDirectory.Url);
						throw new Exception($"Error {response.Error.Code}, '{response.Error.Message}' retrieving for URL: {webDirectory.Url}");
					}

					webDirectory = await ScanAsync(htmlDocument, services, webDirectory);
				}
			}
			else
			{
				webDirectory = await ScanAsync(htmlDocument, services, webDirectory);
			}
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

	private static async Task<string> DecodeResponse(IHtmlDocument htmlDocument, ParserServices services, HttpResponseMessage httpResponseMessage)
	{
		string responseString = await httpResponseMessage.Content.ReadAsStringAsync();
		string responseJson = string.Empty;

		if (responseString.StartsWith('{'))
		{
			responseJson = responseString;
		}
		else
		{
			BhadooState state = services.Session.SiteState.Get<BhadooState>();

			// One engine per scan, and Jint engines are not thread-safe
			await state.Lock.WaitAsync();

			try
			{
				if (state.JintEngine == null)
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
					) ?? throw new Exception("Couldn't find the right JavaScript files");

					state.Obfuscated = appJsScript.Source.Contains("obf.");

					string appJsSource = await services.Http.GetStringAsync(appJsScript.Source.Replace("obf.", string.Empty));

					Parser javaScriptParser = new();
					Script program = javaScriptParser.ParseScript(appJsSource);
					IEnumerable<FunctionDeclaration> javaScriptFunctions = program.ChildNodes.OfType<FunctionDeclaration>();
					FunctionDeclaration readFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "read"));
					string readFunction = appJsSource[readFunctionDeclaration.Range.Start..readFunctionDeclaration.Range.End];

					state.JintEngine = new Engine();

					state.JintEngine.Execute(readFunction);

					if (state.Obfuscated)
					{
						// A plain JavaScript function: a .NET delegate would be bound by reflection, which trimming can break
						state.JintEngine.SetValue("atob", new ClrFunction(state.JintEngine, "atob", (_, arguments) =>
							Encoding.Latin1.GetString(Convert.FromBase64String(TypeConverter.ToString(arguments.At(0)))), length: 1));

						FunctionDeclaration gdidecodeFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "gdidecode"));
						string gdidecodeFunction = appJsSource[gdidecodeFunctionDeclaration.Range.Start..gdidecodeFunctionDeclaration.Range.End];
						state.JintEngine.Execute(gdidecodeFunction);
					}
				}

				JsValue jsValue = state.JintEngine.Invoke("read", responseString);

				if (state.Obfuscated)
				{
					jsValue = state.JintEngine.Invoke("gdidecode", jsValue.ToString());
					responseJson = jsValue.ToString();
				}
				else
				{
					responseJson = Encoding.UTF8.GetString(Convert.FromBase64String(jsValue.ToString()));
				}
			}
			finally
			{
				state.Lock.Release();
			}
		}

		return responseJson;
	}

	private static async Task<WebDirectory> ScanAsync(IHtmlDocument htmlDocument, ParserServices services, WebDirectory webDirectory)
	{
		webDirectory.Parser = Parser;

		try
		{
			RetryPolicy asyncRetryPolicy = RetryPolicy.Create((ex, waitTimeSpan, retry) =>
			{
				if (ex is not SilentException)
				{
					services.Logger.LogWarning("Error retrieving directory listing for '{url}', waiting {waitTime:F0} seconds.. Error: {error}", webDirectory.Uri, waitTimeSpan.TotalSeconds, ex.Message);
					services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.AddDelay(waitTimeSpan);
				}
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
					await services.Session.SiteState.Get<GoogleDriveIndexState>().RateLimiter.RateLimit();

					services.Logger.LogWarning("Retrieving listings for {relativeUrl}, page {page} with password: {password}", webDirectory.Uri.PathAndQuery, pageIndex + 1, services.Session.SiteState.Get<IndexPasswordState>().Password);

					Dictionary<string, string> postValues = new()
					{
						{ "password", services.Session.SiteState.Get<IndexPasswordState>().Password },
						{ "page_token", nextPageToken },
						{ "page_index", pageIndex.ToString() }
					};

					HttpRequestMessage httpRequestMessage = new(HttpMethod.Post, webDirectory.Uri) { Content = new FormUrlEncodedContent(postValues) };
					HttpResponseMessage httpResponseMessage = await services.Http.SendAsync(httpRequestMessage);

					webDirectory.ParsedSuccessfully = httpResponseMessage.IsSuccessStatusCode;

					await RetryAfter.WaitIfAskedAsync(services.Logger, "X", webDirectory, httpResponseMessage);

					httpResponseMessage.EnsureSuccessStatusCode();

					try
					{
						string responseJson = await DecodeResponse(htmlDocument, services, httpResponseMessage);

						BhadooIndexResponse indexResponse = BhadooIndexResponse.FromJson(responseJson);

						webDirectory.ParsedSuccessfully = indexResponse.Data?.Error == null && indexResponse.Error == null;

						if (indexResponse.Data?.Error?.Message == "Rate Limit Exceeded")
						{
							throw new Exception("Rate limit exceeded");
						}
						else if (indexResponse.Error != null)
						{
							webDirectory.Error = true;
							services.Logger.LogError("Error {errprCode}, '{errorMessage}' retrieving for '{url}'", indexResponse.Error.Code, indexResponse.Error.Message, webDirectory.Url);
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
											FileSize = file.Size,
											Description = GoogleDriveIndexMapping.GetFriendlyMimeTypeName(file.MimeType)
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
			if (ex is not SilentException)
			{
				services.Logger.LogError(ex, "Error retrieving directory listing for {url}", webDirectory.Url);
				webDirectory.Error = true;

				services.Session.ReportError(webDirectory.Url);
			}

			//throw;
		}

		return webDirectory;
	}

	// Drive names are unconstrained and can contain '/', '%', '#', etc. - previously only '#' and '/' were
	// neutralized, leaving e.g. a literal '%' not followed by hex digits to later make decodeURIComponent()
	// throw client-side (breaking the web viewer's breadcrumbs). Uri.EscapeDataString handles all of this.
	private static string GetSafeName(string name) => Uri.EscapeDataString(name);
}

/// <summary>The site's own decoding functions, loaded into a JavaScript engine once per scan</summary>
public sealed class BhadooState
{
	public SemaphoreSlim Lock { get; } = new(1, 1);

	public Engine JintEngine { get; set; }

	public bool Obfuscated { get; set; }
}
