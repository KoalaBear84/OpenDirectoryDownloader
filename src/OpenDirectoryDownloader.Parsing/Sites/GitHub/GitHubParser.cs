using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Urls;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenDirectoryDownloader.Parsing.Sites.GitHub;

public static class GitHubParser
{
	private const string Parser = "GitHub";

	public static async Task<WebDirectory> ParseIndex(ParserServices services, WebDirectory webDirectory, string token = null)
	{
		GitHubState state = services.Session.SiteState.Get<GitHubState>();

		try
		{
			if (string.IsNullOrEmpty(state.Owner))
			{
				if (webDirectory.Uri.Segments.Length < 3)
				{
					throw new CancelException("Invalid GitHub url");
				}

				state.Owner = webDirectory.Uri.Segments[1].TrimEnd('/');
				state.Repository = webDirectory.Uri.Segments[2].TrimEnd('/');

				webDirectory.Url = $"https://{Constants.GitHubDomain}/{Uri.EscapeDataString(state.Owner)}/{Uri.EscapeDataString(state.Repository)}/";

				if (!string.IsNullOrWhiteSpace(token))
				{
					services.Logger.LogWarning("Using provided GitHub token for higher rate limits");
					state.Token = token;
				}

				services.Logger.LogWarning("Retrieving default branch");
				HttpResponseMessage httpResponseMessage = await DoRequest(services, GetApiUrl(state.Owner, state.Repository));

				string json = await httpResponseMessage.Content.ReadAsStringAsync();
				using (JsonDocument defaultBranchDocument = JsonDocument.Parse(json))
				{
					state.DefaultBranch = defaultBranchDocument.RootElement.TryGetProperty("default_branch", out JsonElement defaultBranchElement) ? defaultBranchElement.GetString() : null;
				}

				if (string.IsNullOrEmpty(state.DefaultBranch))
				{
					throw new Exception("Invalid default branch");
				}

				services.Logger.LogWarning("Default branch: {defaultBranch}", state.DefaultBranch);

				services.Logger.LogWarning("Retrieving last commit SHA");

				httpResponseMessage = await DoRequest(services, $"{GetApiUrl(state.Owner, state.Repository)}/branches/{state.DefaultBranch}");

				json = await httpResponseMessage.Content.ReadAsStringAsync();
				using (JsonDocument commitDocument = JsonDocument.Parse(json))
				{
					state.CurrentCommitSha = commitDocument.RootElement.TryGetProperty("commit", out JsonElement commitElement) && commitElement.TryGetProperty("sha", out JsonElement shaElement) ? shaElement.GetString() : null;
				}

				if (string.IsNullOrEmpty(state.CurrentCommitSha))
				{
					throw new Exception("Empty repository");
				}

				services.Logger.LogWarning("Last commit SHA: {commitSha}", state.CurrentCommitSha);
			}

			webDirectory = await ScanAsync(services, webDirectory);
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

	private static string GetHeader(HttpResponseHeaders httpResponseHeaders, string headerName)
	{
		return httpResponseHeaders.Contains(headerName) ? httpResponseHeaders.GetValues(headerName).FirstOrDefault() : string.Empty;
	}

	private static async Task<HttpResponseMessage> DoRequest(ParserServices services, string url)
	{
		bool rateLimit = false;
		HttpResponseMessage httpResponseMessage;

		do
		{
			// The GitHub API wants its own headers; set per request, the client's defaults are shared with everything else
			using HttpRequestMessage request = new(HttpMethod.Get, url);
			request.Headers.Accept.ParseAdd("application/vnd.github+json");
			request.Headers.UserAgent.ParseAdd("OpenDirectoryDownloader");

			if (!string.IsNullOrWhiteSpace(services.Session.SiteState.Get<GitHubState>().Token))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("token", services.Session.SiteState.Get<GitHubState>().Token);
			}

			httpResponseMessage = await services.Http.SendAsync(request);

			if (httpResponseMessage.Headers.Contains("X-RateLimit-Remaining"))
			{
				services.Logger.LogWarning("RateLimit remaining: {rateLimitRemaining}/{rateLimitTotal}", GetHeader(httpResponseMessage.Headers, "X-RateLimit-Remaining"), GetHeader(httpResponseMessage.Headers, "X-RateLimit-Limit"));
			}

			if (httpResponseMessage.StatusCode == HttpStatusCode.Unauthorized)
			{
				throw new CancelException($"Bad GitHub token");
			}

			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				if (httpResponseMessage.Headers.Contains("X-RateLimit-Reset"))
				{
					rateLimit = true;
					DateTimeOffset resetDateTime = UrlHelper.UnixTimestampToDateTime(long.Parse(GetHeader(httpResponseMessage.Headers, "X-RateLimit-Reset")));
					DateTimeOffset currentDate = httpResponseMessage.Headers.Date ?? DateTimeOffset.UtcNow;

					// Use Server time if possible, add 5 seconds of slack
					TimeSpan rateLimitTimeSpan = resetDateTime - (currentDate) + TimeSpan.FromSeconds(5);

					if (rateLimitTimeSpan.TotalMilliseconds < TimeSpan.FromSeconds(5).TotalMilliseconds)
					{
						rateLimitTimeSpan = TimeSpan.FromSeconds(5);
					}

					resetDateTime = currentDate + rateLimitTimeSpan;

					services.Logger.LogWarning("Rate limited, waiting until {untilDate}.. Increase rate limits by using a token: https://github.com/settings/tokens/new (no scopes required)", resetDateTime.ToLocalTime().ToString(Constants.DateTimeFormat));

					services.Session.StatisticsSuppressed = true;
					await Task.Delay(rateLimitTimeSpan);
					services.Session.StatisticsSuppressed = false;
				}
			}
			else
			{
				rateLimit = false;
			}
		} while (rateLimit);

		return httpResponseMessage;
	}

	private static async Task<WebDirectory> ScanAsync(ParserServices services, WebDirectory webDirectory)
	{
		GitHubState state = services.Session.SiteState.Get<GitHubState>();

		services.Logger.LogDebug("Retrieving listings for {url}", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			// There is NO paging available. Probably also not needed, but still..

			string sha = state.CurrentCommitSha;

			if (webDirectory.Uri.Segments.Length == 7)
			{
				sha = webDirectory.Uri.Segments.Last();
			}

			// Setting recursive to any value returns more than current limit (100.000)
			string url = $"{GetApiUrl(state.Owner, state.Repository)}/git/trees/{sha}?recursive=true";

			HttpResponseMessage httpResponseMessage = await DoRequest(services, url);

			httpResponseMessage.EnsureSuccessStatusCode();

			string json = await httpResponseMessage.Content.ReadAsStringAsync();

			GitHubResult gitHubResult = GitHubResult.FromJson(json);

			if (gitHubResult.Truncated)
			{
				services.Logger.LogWarning("GitHub response is truncated with {items} items, sadly there is no paging available..", gitHubResult.Tree.Length);
			}

			WebDirectory currentWebDirectory = webDirectory;

			// Yes, this code is a little complicated, but it works..
			foreach (Tree treeItem in gitHubResult.Tree)
			{
				WebDirectory parentWebDirectory = currentWebDirectory;

				// Like directories
				if (treeItem.Type == "tree")
				{
					while (parentWebDirectory.ParentDirectory is not null && !treeItem.Path.StartsWith(parentWebDirectory.Name))
					{
						parentWebDirectory = parentWebDirectory.ParentDirectory;
					}

					WebDirectory newWebDirectory = new(parentWebDirectory)
					{
						Parser = Parser,
						Url = treeItem.Url,
						Name = treeItem.Path
					};

					parentWebDirectory.Subdirectories.Add(newWebDirectory);
					currentWebDirectory = newWebDirectory;
				}
				else
				// Like files
				{
					while (parentWebDirectory.ParentDirectory is not null && !Path.GetDirectoryName(treeItem.Path).Replace("\\", "/").StartsWith(parentWebDirectory.Name))
					{
						parentWebDirectory = parentWebDirectory.ParentDirectory;
					}

					string fileName = new Uri(new Uri($"https://raw.githubusercontent.com/{Uri.EscapeDataString(state.Owner)}/{Uri.EscapeDataString(state.Repository)}/"), Path.Combine(state.CurrentCommitSha, treeItem.Path)).ToString();

					parentWebDirectory.Files.Add(new WebFile
					{
						Url = fileName,
						FileName = treeItem.Path,
						FileSize = treeItem.Size
					});
				}
			}
		}
		catch (Exception ex)
		{
			services.Logger.LogError(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
			webDirectory.Error = true;

			services.Session.ReportError(webDirectory.Url);

			//throw;
		}

		return webDirectory;
	}

	private static string GetApiUrl(string owner, string repository) => $"https://api.{Constants.GitHubDomain}/repos/{WebUtility.UrlEncode(owner)}/{WebUtility.UrlEncode(repository)}";
}

/// <summary>The repository being scanned, read from the first URL</summary>
public sealed class GitHubState
{
	public string Owner { get; set; }

	public string Repository { get; set; }

	public string DefaultBranch { get; set; }

	public string CurrentCommitSha { get; set; }

	public string Token { get; set; }
}
