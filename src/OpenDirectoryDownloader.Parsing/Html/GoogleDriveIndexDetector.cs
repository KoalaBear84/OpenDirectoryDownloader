using Acornima;
using Acornima.Ast;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Parsing.Sites;
using OpenDirectoryDownloader.Parsing.Sites.GDIndex;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Parsing.Html;

/// <summary>
/// Recognizes the Google Drive index front ends by their scripts: by name, by the sources in an app.min.js source
/// map, or by a gdidecode function in app.js. A script is downloaded and checked once per scan, not for every page.
/// </summary>
internal static partial class GoogleDriveIndexDetector
{
	/// <returns>One of the <see cref="GoogleDriveIndexMapping"/> names, or null</returns>
	public static async Task<string> DetectAsync(ParseContext context)
	{
		GoogleDriveIndexState state = context.Services.Session.SiteState.Get<GoogleDriveIndexState>();

		foreach (IHtmlScriptElement script in context.Document.Scripts.Where(s => s.Source is not null))
		{
			string googleDriveIndexType = GoogleDriveIndexMapping.GetGoogleDriveIndexType(script.Source);

			if (googleDriveIndexType is null && script.Source.Contains("app.min.js", StringComparison.InvariantCultureIgnoreCase))
			{
				googleDriveIndexType = await CheckOnceAsync(state, $"sourcemap|{script.Source}", () => CheckSourceMapAsync(context, script.Source));
			}

			if (googleDriveIndexType is null && script.Source.Contains("app.js", StringComparison.InvariantCultureIgnoreCase))
			{
				googleDriveIndexType = await CheckOnceAsync(state, $"appjs|{script.Source}", () => CheckAppJsAsync(context, script.Source));
			}

			if (googleDriveIndexType is not null)
			{
				return googleDriveIndexType;
			}
		}

		return null;
	}

	/// <summary>Runs <paramref name="check"/> once per key; a check that failed is tried again next time, as it would have been before.</summary>
	private static async Task<string> CheckOnceAsync(GoogleDriveIndexState state, string key, Func<Task<string>> check)
	{
		Lazy<Task<string>> lazyCheck = state.CheckedScripts.GetOrAdd(key, _ => new Lazy<Task<string>>(check));

		try
		{
			return await lazyCheck.Value;
		}
		catch
		{
			state.CheckedScripts.TryRemove(key, out _);
			throw;
		}
	}

	private static async Task<string> CheckSourceMapAsync(ParseContext context, string scriptUrl)
	{
		context.Logger.LogWarning("Checking/downloading javascript for sourcemaps: {scriptUrl}", scriptUrl);
		string sourceMapUrl = await GetSourceMapUrlFromJavaScriptAsync(context.Services.Http, scriptUrl);

		if (string.IsNullOrWhiteSpace(sourceMapUrl))
		{
			return null;
		}

		string fullSourceMapUrl = new Uri(new Uri(scriptUrl), sourceMapUrl).ToString();
		context.Logger.LogWarning("Checking/downloading sourcemap for known Google Drive index: {sourceMapUrl}", fullSourceMapUrl);

		await using Stream httpStream = await context.Services.Http.GetStreamAsync(fullSourceMapUrl);
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(httpStream);

		if (!jsonDocument.RootElement.TryGetProperty("sources", out JsonElement sources))
		{
			return null;
		}

		foreach (JsonElement source in sources.EnumerateArray())
		{
			string googleDriveIndexType = GoogleDriveIndexMapping.GetGoogleDriveIndexType(source.GetString());

			if (googleDriveIndexType is not null)
			{
				return googleDriveIndexType;
			}
		}

		return null;
	}

	private static async Task<string> CheckAppJsAsync(ParseContext context, string scriptUrl)
	{
		if (Uri.IsWellFormedUriString(scriptUrl, UriKind.Relative))
		{
			scriptUrl = new Uri(context.Services.Session.Root.Uri, scriptUrl).ToString();
		}

		string appJsSource = await context.Services.Http.GetStringAsync(scriptUrl);

		Parser javaScriptParser = new();
		Script program = javaScriptParser.ParseScript(appJsSource);
		IEnumerable<FunctionDeclaration> javaScriptFunctions = program.ChildNodes.OfType<FunctionDeclaration>();
		FunctionDeclaration gdidecodeFunctionDeclaration = javaScriptFunctions.FirstOrDefault(f => f.ChildNodes.OfType<Identifier>().Any(i => i.Name == "gdidecode"));

		return gdidecodeFunctionDeclaration is not null ? GoogleDriveIndexMapping.BhadooIndex : null;
	}

	private static async Task<string> GetSourceMapUrlFromJavaScriptAsync(HttpClient httpClient, string url)
	{
		using HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url);

		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			return null;
		}

		string javaScript = await httpResponseMessage.Content.ReadAsStringAsync();

		Match regexMatch = SourceMappingUrlRegex().Match(javaScript);

		return !regexMatch.Success ? null : regexMatch.Groups["SourceMapUrl"].Value;
	}

	[GeneratedRegex(@"\/\/# sourceMappingURL=(?<SourceMapUrl>.*)")]
	private static partial Regex SourceMappingUrlRegex();
}
