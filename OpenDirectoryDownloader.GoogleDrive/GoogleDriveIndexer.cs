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

namespace OpenDirectoryDownloader.GoogleDrive
{
    public static class GoogleDriveIndexer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // If modifying these scopes, delete your previously saved credentials
        // at ~/.credentials/drive-dotnet-quickstart.json
        static readonly string[] Scopes = { DriveService.Scope.DriveMetadataReadonly };
        static readonly DriveService DriveService;
        static readonly string ApplicationName = "OpenDirectoryDownloader";
        const string FolderMimeType = "application/vnd.google-apps.folder";
        static readonly RateLimiter RateLimiter = new RateLimiter(900, TimeSpan.FromSeconds(100));

        static GoogleDriveIndexer()
        {
            UserCredential credential;

            using (FileStream fileStream = new FileStream("OpenDirectoryDownloader.GoogleDrive.json", FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = "token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(fileStream).Secrets,
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

        public static async Task<WebDirectory> IndexAsync(WebDirectory webDirectory)
        {
            webDirectory.StartTime = DateTimeOffset.UtcNow;
            string nextPageToken = string.Empty;
            string folderId = webDirectory.Uri.Segments.Last();

            do
            {
                await RateLimiter.RateLimit();

                Logger.Debug($"Started Google Drive Request for Folder {folderId}");

                FilesResource.ListRequest listRequest = DriveService.Files.List();
                listRequest.PageSize = 1000;
                listRequest.Q = $"'{folderId}' in parents";
                listRequest.PageToken = nextPageToken;
                listRequest.Fields = "nextPageToken, files(id, name, mimeType, size)";
                Google.Apis.Drive.v3.Data.FileList fileList = await listRequest.ExecuteAsync();

                foreach (Google.Apis.Drive.v3.Data.File file in fileList.Files.OrderByDescending(f => f.MimeType == FolderMimeType).ThenBy(f => f.Name))
                {
                    bool isFile = file.MimeType != FolderMimeType;

                    if (!isFile)
                    {
                        webDirectory.Subdirectories.Add(new WebDirectory(webDirectory)
                        {
                            Url = $"https://drive.google.com/drive/folders/{file.Id}",
                            Name = file.Name
                        });
                    }
                    else
                    {
                        webDirectory.Files.Add(new WebFile
                        {
                            Url = $"https://drive.google.com/uc?export=download&id={file.Id}",
                            FileName = file.Name,
                            FileSize = file.Size ?? 0
                        });
                    }
                }

                nextPageToken = fileList.NextPageToken;
            } while (!string.IsNullOrWhiteSpace(nextPageToken));

            webDirectory.Finished = true;

            Logger.Debug($"Finished Google Drive Request for Folder {folderId}");

            return webDirectory;
        }
    }
}
