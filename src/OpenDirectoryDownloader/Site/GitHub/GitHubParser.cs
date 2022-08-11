using Newtonsoft.Json.Linq;
using NLog;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.Site.GitHub;

public static class GitHubParser
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private const string Parser = "GitHub";
	private static string Owner { get; set; }
	private static string Repository { get; set; }
	private static string DefaultBranch { get; set; }
	private static string CurrentCommitSha { get; set; }

	public static async Task<WebDirectory> ParseIndex(HttpClient httpClient, WebDirectory webDirectory)
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

				httpClient.DefaultRequestHeaders.Clear();
				httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenDirectoryDownloader");

				Logger.Warn("Retrieving default branch");
				HttpResponseMessage httpResponseMessage = await DoRequest(httpClient, GetApiUrl(Owner, Repository));

				string json = await httpResponseMessage.Content.ReadAsStringAsync();
				DefaultBranch = JObject.Parse(json).SelectToken("default_branch")?.Value<string>();

				if (string.IsNullOrEmpty(DefaultBranch))
				{
					throw new Exception("Invalid default branch");
				}

				Logger.Warn("Retrieving last commit SHA");

				httpResponseMessage = await DoRequest(httpClient, $"{GetApiUrl(Owner, Repository)}/branches/{DefaultBranch}");

				json = await httpResponseMessage.Content.ReadAsStringAsync();
				CurrentCommitSha = JObject.Parse(json).SelectToken("commit.sha")?.Value<string>();

				if (string.IsNullOrEmpty(CurrentCommitSha))
				{
					throw new Exception("Empty repository");
				}
			}

			webDirectory = await ScanAsync(httpClient, webDirectory);
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
				Logger.Warn($"RateLimit remaining: {GetHeader(httpResponseMessage.Headers, "X-RateLimit-Remaining")}/{GetHeader(httpResponseMessage.Headers, "X-RateLimit-Limit")}");
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

					Logger.Warn($"Rate limited, waiting until {resetDateTime.ToLocalTime().ToString(Constants.DateTimeFormat)}..");

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
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

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
				Logger.Warn($"GitHub response is truncated with {gitHubResult.Tree.Length} items, sadly there is no paging available..");
			}

			foreach (Tree treeItem in gitHubResult.Tree)
			{
				// Like directories
				if (treeItem.Type == "tree")
				{
					webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
					{
						Parser = Parser,
						// Use real URL
						Url = treeItem.Url,
						Name = treeItem.Path
					});
				}
				else
				// Like files
				{
					webDirectory.Files.Add(new WebFile
					{
						// Use real URL
						// TODO: Use https://raw.githubusercontent.com/{Owner}/{Repo}/{sha}/{Path}
						Url = treeItem.Url,
						FileName = treeItem.Path,
						FileSize = treeItem.Size
					});
				}
			}
		}
		catch (Exception ex)
		{
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

	private static string GetApiUrl(string owner, string repository) => $"https://api.{Constants.GitHubDomain}/repos/{WebUtility.UrlEncode(owner)}/{WebUtility.UrlEncode(repository)}";
}
