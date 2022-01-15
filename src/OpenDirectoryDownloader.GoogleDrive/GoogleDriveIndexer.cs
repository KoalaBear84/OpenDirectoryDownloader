using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using NLog;
using OpenDirectoryDownloader.Shared;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader.GoogleDrive;

public static class GoogleDriveIndexer
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	// If modifying these scopes, delete your previously saved credentials
	// at ~/.credentials/drive-dotnet-quickstart.json
	static readonly string[] Scopes = { DriveService.Scope.DriveMetadataReadonly };
	static readonly DriveService DriveService;
	static readonly string ApplicationName = "OpenDirectoryDownloader";
	const string FolderMimeType = "application/vnd.google-apps.folder";
	const string ShortcutMimeType = "application/vnd.google-apps.shortcut";
	static readonly RateLimiter RateLimiter = new RateLimiter(900, TimeSpan.FromSeconds(100), 0.9d);

	static GoogleDriveIndexer()
	{
		try
		{
			UserCredential credential;

			using (FileStream fileStream = new FileStream("OpenDirectoryDownloader.GoogleDrive.json", FileMode.Open, FileAccess.Read))
			{
				// The file token.json stores the user's access and refresh tokens, and is created
				// automatically when the authorization flow completes for the first time.
				string credPath = "token.json";
				credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
					GoogleClientSecrets.FromStream(fileStream).Secrets,
					Scopes,
					"user",
					CancellationToken.None,
					new FileDataStore(credPath, true)).Result;

				Console.WriteLine($"Credential file saved to: {credPath}");
			}

			// Create Drive API service.
			DriveService = new DriveService(new BaseClientService.Initializer()
			{
				HttpClientInitializer = credential,
				ApplicationName = ApplicationName,
			});
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error initializing Google Drive, please check OpenDirectoryDownloader.GoogleDrive.json and/or remove the 'token.json' directory. See readme on Github for more help. ERROR: {ex}");
			Logger.Error(ex, "Error initializing Google Drive, please check OpenDirectoryDownloader.GoogleDrive.json and/or remove the 'token.json' directory. See readme on Github for more help.");
			throw;
		}
	}

	public static async Task<WebDirectory> IndexAsync(WebDirectory webDirectory, string resourceKey)
	{
		webDirectory.StartTime = DateTimeOffset.UtcNow;
		string nextPageToken = string.Empty;
		string folderId = webDirectory.Uri.Segments.Last();

		bool rateLimitException = false;

		int retries = 0;
		int maxRetries = 5;

		while (retries < maxRetries || rateLimitException || !string.IsNullOrWhiteSpace(nextPageToken))
		{
			try
			{
				await RateLimiter.RateLimit();

				Logger.Debug($"Started Google Drive Request for Folder {folderId}");

				if (!string.IsNullOrWhiteSpace(resourceKey))
				{
					DriveService.HttpClient.DefaultRequestHeaders.Add("X-Goog-Drive-Resource-Keys", $"{folderId}/{resourceKey}");
				}
				else
				{
					if (DriveService.HttpClient.DefaultRequestHeaders.Contains("X-Goog-Drive-Resource-Keys"))
					{
						DriveService.HttpClient.DefaultRequestHeaders.Remove("X-Goog-Drive-Resource-Keys");
					}
				}

				FilesResource.ListRequest listRequest = DriveService.Files.List();
				listRequest.PageSize = 1000;
				listRequest.Q = $"'{folderId}' in parents";
				listRequest.PageToken = nextPageToken;
				listRequest.Fields = "nextPageToken, files(id, name, mimeType, size, shortcutDetails, resourceKey)";
				listRequest.IncludeItemsFromAllDrives = true;
				listRequest.SupportsAllDrives = true;
				Google.Apis.Drive.v3.Data.FileList fileList = await listRequest.ExecuteAsync();

				foreach (Google.Apis.Drive.v3.Data.File file in fileList.Files.OrderByDescending(f => f.MimeType == FolderMimeType || f.MimeType == ShortcutMimeType).ThenBy(f => f.Name))
				{
					string mimeType = file.ShortcutDetails?.TargetMimeType ?? file.MimeType;
					string id = file.ShortcutDetails?.TargetId ?? file.Id;

					bool isFile = mimeType != FolderMimeType;

					if (!isFile)
					{
						string url = $"https://drive.google.com/drive/folders/{id}";

						if (!string.IsNullOrEmpty(file.ResourceKey))
						{
							url += $"?resourcekey={file.ResourceKey}";
						}

						webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
						{
							Url = url,
							Name = file.Name
						});
					}
					else
					{
						string url = $"https://drive.google.com/uc?export=download&id={id}";

						if (!string.IsNullOrEmpty(file.ResourceKey))
						{
							url += $"&resourcekey={file.ResourceKey}";
						}

						webDirectory.Files.Add(new WebFile
						{
							Url = url,
							FileName = file.Name,
							FileSize = file.Size ?? 0
						});
					}
				}

				nextPageToken = fileList.NextPageToken;

				rateLimitException = false;

				if (retries > 0)
				{
					Logger.Warn($"Retrieval succesful after try {retries + 1} for {webDirectory.Url}");
				}

				if (string.IsNullOrWhiteSpace(nextPageToken))
				{
					break;
				}

				retries = 0;
			}
			catch (GoogleApiException ex)
			{
				rateLimitException = ex.Error?.Message == "User rate limit exceeded.";

				if (rateLimitException)
				{
					Logger.Debug($"Google Drive rate limit, try again");
				}
				else
				{
					retries++;
					Logger.Warn($"Google Drive error for {webDirectory.Url} on try {retries + 1}: {ex.Message}");
				}

				if (retries == maxRetries)
				{
					Logger.Error($"Skip {webDirectory.Url} because of {maxRetries} consecutive errors on : {ex.Message}");
					return webDirectory;
				}
			}
		}

		Logger.Debug($"Finished Google Drive Request for Folder {folderId}");

		return webDirectory;
	}
}
