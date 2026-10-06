using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Models;
using OpenDirectoryDownloader.Threading;

namespace OpenDirectoryDownloader.GoogleDrive;

/// <summary>Lists Google Drive folders through the Drive API (needs OpenDirectoryDownloader.GoogleDrive.json, see the readme).</summary>
public sealed class GoogleDriveIndexer
{
	// If modifying these scopes, delete your previously saved credentials in token.json
	private static readonly string[] Scopes = [DriveService.Scope.DriveMetadataReadonly];
	private static readonly string ApplicationName = "OpenDirectoryDownloader";
	private const string FolderMimeType = "application/vnd.google-apps.folder";
	private const string ShortcutMimeType = "application/vnd.google-apps.shortcut";

	private readonly DriveService DriveService;
	private readonly RateLimiter RateLimiter = new(900, TimeSpan.FromSeconds(100), 0.9d);
	private readonly ILogger Logger;

	private GoogleDriveIndexer(DriveService driveService, ILogger logger)
	{
		DriveService = driveService;
		Logger = logger;
	}

	/// <summary>Signs in (the first time in a browser, after that from token.json) and connects to the Drive API.</summary>
	public static async Task<GoogleDriveIndexer> CreateAsync(ILogger logger, CancellationToken cancellationToken = default)
	{
		try
		{
			using FileStream fileStream = new("OpenDirectoryDownloader.GoogleDrive.json", FileMode.Open, FileAccess.Read);

			// token.json stores the user's access and refresh tokens, it is created when the authorization flow completes for the first time
			string credPath = "token.json";
			UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
				GoogleClientSecrets.FromStream(fileStream).Secrets,
				Scopes,
				"user",
				cancellationToken,
				new FileDataStore(credPath, true),
				new HeadlessLocalServerCodeReceiver());

			Console.WriteLine($"Credential file saved to: {credPath}");

			DriveService driveService = new(new BaseClientService.Initializer()
			{
				HttpClientInitializer = credential,
				ApplicationName = ApplicationName,
			});

			return new GoogleDriveIndexer(driveService, logger);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error initializing Google Drive, please check OpenDirectoryDownloader.GoogleDrive.json and/or remove the 'token.json' directory. See readme on Github for more help. ERROR: {ex}");
			logger.LogError(ex, "Error initializing Google Drive, please check OpenDirectoryDownloader.GoogleDrive.json and/or remove the 'token.json' directory. See readme on Github for more help.");
			throw;
		}
	}

	public async Task<WebDirectory> IndexAsync(WebDirectory webDirectory, string resourceKey)
	{
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

				Logger.LogDebug("Started Google Drive Request for Folder {folderId}", folderId);

				FilesResource.ListRequest listRequest = DriveService.Files.List();

				// Per request: the client is shared by the workers
				if (!string.IsNullOrWhiteSpace(resourceKey))
				{
					listRequest.ModifyRequest = request => request.Headers.TryAddWithoutValidation("X-Goog-Drive-Resource-Keys", $"{folderId}/{resourceKey}");
				}

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
					Logger.LogWarning("Retrieval succesful after try {retries} for {url}", retries + 1, webDirectory.Url);
				}

				if (string.IsNullOrWhiteSpace(nextPageToken))
				{
					break;
				}

				retries = 0;
			}
			catch (GoogleApiException ex)
			{
				RateLimiter.AddDelay(TimeSpan.FromSeconds(5));
				rateLimitException = ex.Error?.Message == "User rate limit exceeded.";

				if (rateLimitException)
				{
					Logger.LogDebug("Google Drive rate limit, try again");
				}
				else
				{
					retries++;
					Logger.LogWarning("Google Drive error for {url} on try {retries}: {error}", webDirectory.Url, retries + 1, ex.Message);
				}

				if (retries == maxRetries)
				{
					Logger.LogError("Skip {url} because of {maxRetries} consecutive errors on : {error}", webDirectory.Url, maxRetries, ex.Message);
					webDirectory.Error = true;
					return webDirectory;
				}
			}
		}

		Logger.LogDebug("Finished Google Drive Request for Folder {folderId}", folderId);

		return webDirectory;
	}
}
