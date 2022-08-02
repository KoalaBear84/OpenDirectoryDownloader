using Newtonsoft.Json.Linq;
using NLog;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
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
					throw new Exception("Invalid GitHub url");
				}

				Owner = webDirectory.Uri.Segments[1].TrimEnd('/');
				Repository = webDirectory.Uri.Segments[2].TrimEnd('/');

				httpClient.DefaultRequestHeaders.Clear();
				httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent.Curl);

				Logger.Warn("Retrieving default branch");

				string json = await httpClient.GetStringAsync(GetApiUrl(Owner, Repository));
				DefaultBranch = JObject.Parse(json).SelectToken("default_branch")?.Value<string>();

				if (string.IsNullOrEmpty(DefaultBranch))
				{
					throw new Exception("Invalid default branch");
				}

				Logger.Warn("Retrieving last commit SHA");

				json = await httpClient.GetStringAsync($"{GetApiUrl(Owner, Repository)}/branches/{DefaultBranch}");
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

	private static async Task<WebDirectory> ScanAsync(HttpClient httpClient, WebDirectory webDirectory)
	{
		Logger.Debug($"Retrieving listings for {webDirectory.Uri}");

		webDirectory.Parser = Parser;

		try
		{
			// TODO: Add paging if needed
			bool hasNextPage = false;

			do
			{
				//https://api.github.com/repos/KoalaBear84/OpenDirectoryDownloader/git/trees/39a621f7664d439e0617256f0364483c86646d4f

				string url = $"{GetApiUrl(Owner, Repository)}/git/trees/{CurrentCommitSha}";

				if (webDirectory.Uri.Segments.Length == 7)
				{
					string treeSha = webDirectory.Uri.Segments.Last();
					url = $"{GetApiUrl(Owner, Repository)}/git/trees/{treeSha}";
				}

				HttpResponseMessage httpResponseMessage = await httpClient.GetAsync(url);

				httpResponseMessage.EnsureSuccessStatusCode();

				string json = await httpResponseMessage.Content.ReadAsStringAsync();

				GitHubResult gitHubResult = GitHubResult.FromJson(json);

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
							Url = treeItem.Url,
							FileName = treeItem.Path,
							FileSize = treeItem.Size
						});
					}
				}
			} while (hasNextPage);
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
