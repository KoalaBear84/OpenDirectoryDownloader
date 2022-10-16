using Newtonsoft.Json.Linq;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared.Models;
using System.Net;
using System.Net.Http.Headers;

namespace OpenDirectoryDownloader.Site.GitHub;

public static class GitHubParser
{
	private const string Parser = "GitHub";
	private static string Owner { get; set; }
	private static string Repository { get; set; }
	private static string DefaultBranch { get; set; }
	private static string CurrentCommitSha { get; set; }

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory, string token = null)
	{
		try
		{
			if (string.IsNullOrEmpty(Owner))
			{
				if (webDirectory.Uri.Segments.Length < 3)
				{
					throw new CancelException("Invalid GitHub url");
				}

				Owner = webDirectory.Uri.Segments[1].TrimEnd('/');
				Repository = webDirectory.Uri.Segments[2].TrimEnd('/');

				webDirectory.Url = $"https://{Constants.GitHubDomain}/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Repository)}/";

				httpClient.DefaultRequestHeaders.Clear();
				httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
				httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenDirectoryDownloader");

				if (!string.IsNullOrWhiteSpace(token))
				{
					Program.Logger.Warning("Using provided GitHub token for higher rate limits");
					httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
				}

				Program.Logger.Warning("Retrieving default branch");
				HttpResponseMessage httpResponseMessage = await DoRequest(httpClient, GetApiUrl(Owner, Repository));

				string json = await httpResponseMessage.Content.ReadAsStringAsync();
				DefaultBranch = JObject.Parse(json).SelectToken("default_branch")?.Value<string>();

				if (string.IsNullOrEmpty(DefaultBranch))
				{
					throw new Exception("Invalid default branch");
				}

				Program.Logger.Warning("Default branch: {defaultBranch}", DefaultBranch);

				Program.Logger.Warning("Retrieving last commit SHA");

				httpResponseMessage = await DoRequest(httpClient, $"{GetApiUrl(Owner, Repository)}/branches/{DefaultBranch}");

				json = await httpResponseMessage.Content.ReadAsStringAsync();
				CurrentCommitSha = JObject.Parse(json).SelectToken("commit.sha")?.Value<string>();

				if (string.IsNullOrEmpty(CurrentCommitSha))
				{
					throw new Exception("Empty repository");
				}

				Program.Logger.Warning("Last commit SHA: {commitSha}", CurrentCommitSha);
			}

			webDirectory = await ScanAsync(httpClient, webDirectory);
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

	private static string GetHeader(HttpResponseHeaders httpResponseHeaders, string headerName)
	{
		return httpResponseHeaders.Contains(headerName) ? httpResponseHeaders.GetValues(headerName).FirstOrDefault() : string.Empty;
	}

	private static async Task<HttpResponseMessage> DoRequest(HttpClient httpClient, string url)
	{
		bool rateLimit = false;
		HttpResponseMessage httpResponseMessage;

		do
		{
			httpResponseMessage = await httpClient.GetAsync(url);

			if (httpResponseMessage.Headers.Contains("X-RateLimit-Remaining"))
			{
				Program.Logger.Warning("RateLimit remaining: {rateLimitRemaining}/{rateLimitTotal}", GetHeader(httpResponseMessage.Headers, "X-RateLimit-Remaining"), GetHeader(httpResponseMessage.Headers, "X-RateLimit-Limit"));
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
					DateTimeOffset resetDateTime = Library.UnixTimestampToDateTime(long.Parse(GetHeader(httpResponseMessage.Headers, "X-RateLimit-Reset")));
					DateTimeOffset currentDate = httpResponseMessage.Headers.Date ?? DateTimeOffset.UtcNow;

					// Use Server time if possible, add 5 seconds of slack
					TimeSpan rateLimitTimeSpan = resetDateTime - (currentDate) + TimeSpan.FromSeconds(5);

					if (rateLimitTimeSpan.TotalMilliseconds < TimeSpan.FromSeconds(5).TotalMilliseconds)
					{
						rateLimitTimeSpan = TimeSpan.FromSeconds(5);
					}

					resetDateTime = currentDate + rateLimitTimeSpan;

					Program.Logger.Warning("Rate limited, waiting until {untilDate}.. Increase rate limits by using a token: https://github.com/settings/tokens/new (no scopes required)", resetDateTime.ToLocalTime().ToString(Constants.DateTimeFormat));

					OpenDirectoryIndexer.ShowStatistics = false;
					await Task.Delay(rateLimitTimeSpan);
					OpenDirectoryIndexer.ShowStatistics = true;
				}
			}
			else
			{
				rateLimit = false;
			}
		} while (rateLimit);

		return httpResponseMessage;
	}

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Program.Logger.Debug("Retrieving listings for {url}", webDirectory.Uri);

		webDirectory.Parser = Parser;

		try
		{
			// There is NO paging available. Probably also not needed, but still..

			string sha = CurrentCommitSha;

			if (webDirectory.Uri.Segments.Length == 7)
			{
				sha = webDirectory.Uri.Segments.Last();
			}

			// Setting recursive to any value returns more than current limit (100.000)
			string url = $"{GetApiUrl(Owner, Repository)}/git/trees/{sha}?recursive=true";

			HttpResponseMessage httpResponseMessage = await DoRequest(httpClient, url);

			httpResponseMessage.EnsureSuccessStatusCode();

			string json = await httpResponseMessage.Content.ReadAsStringAsync();

			GitHubResult gitHubResult = GitHubResult.FromJson(json);

			if (gitHubResult.Truncated)
			{
				Program.Logger.Warning("GitHub response is truncated with {items} items, sadly there is no paging available..", gitHubResult.Tree.Length);
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

					WebDirectory newWebDirectory = new WebDirectory(parentWebDirectory)
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

					string fileName = new Uri(new Uri($"https://raw.githubusercontent.com/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Repository)}/"), Path.Combine(CurrentCommitSha, treeItem.Path)).ToString();

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
			Program.Logger.Error(ex, "Error processing {parser} for {url}", Parser, webDirectory.Url);
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

	private static string GetApiUrl(string owner, string repository) => $"https://api.{Constants.GitHubDomain}/repos/{WebUtility.UrlEncode(owner)}/{WebUtility.UrlEncode(repository)}";
}
