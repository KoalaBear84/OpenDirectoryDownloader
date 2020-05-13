# Open Directory Downloader
Indexes open directories listings in 80+ supported formats, including FTP, Google Drive.

Written in C# with .NET Core, which means it is **cross platform**!

Downloading is not (yet) implemented, but is already possible when you use the resulting file into another tool (for most of the formats).

Downloading with [aria2c](https://aria2.github.io/):
`aria2c -i theurlsfile.txt`

Downloading with wget:
`wget -x -i theurlsfile.txt`

If you have improvements, supply me with a pull request! If you have a format not yet supported, please let me know.

## Prerequisites

Please install the latest version of .NET Core 3.1.

https://dotnet.microsoft.com/download/dotnet-core/3.1

## Usage

Command line parameters:

| Short | Long | Description
|---------|----------|---------
| `-u` | `--url` | Url to scan
| `-t` | `--threads` | Number of threads
| `-o` | `--timeout` | Number of seconds for timeout
| `-q` | `--quit` | Do not wait after scanning
| `-j` | `--json` | Save JSON file
| `-f` | `--no-urls` | Do not save URLs file
| `-r` | `--no-reddit` | Do not show Reddit stats markdown
| `-l` | `--upload-urls` | Uploads urls file
| `-e` | `--exact-file-sizes` | Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)
| `-s` | `--speed-test` | Does a speed test after indexing
| | `--username` | Username
| | `--password` | Password

Example:
`OpenDirectoryIndexer.exe --url "https://myopendirectory.com"`

If you want to learn more or contribute, see the following paragraphs!

## Getting the code

### For Visual Studio (Windows)
1. Install Visual Studio: https://visualstudio.microsoft.com/vs/community/
  a. With workload: ".NET Core cross-platform development"
  b. With individual components: Code tools > Git for Windows and Code tools > GitHub extension for Visual Studio
2. Be sure to install Git: https://git-scm.com/downloads
3. Clone the repository by clicking "Clone or download" and click "Open in Visual Studio"

### For Visual Studio Code
1. Download Visual Studio Code: https://code.visualstudio.com/download
2. Be sure to install Git: https://git-scm.com/downloads
3. Clone the repository: https://code.visualstudio.com/docs/editor/versioncontrol#_cloning-a-repository
4. More help: https://docs.microsoft.com/en-us/dotnet/core/tutorials/with-visual-studio-code

## Building on Linux
1. Install the newest preview of .NET Core 3.0. 
2. `git clone https://github.com/KoalaBear84/OpenDirectoryDownloader`
3. `cd OpenDirectoryDownloader/OpenDirectoryDownloader`
4. `dotnet build .`
5. `cd bin/Debug/netcoreapp3.0`
6. `./OpenDirectoryDownloader --url "https://myopendirectory.com"`

Then, if you need to package it into a binary, you can use [warp-packer](https://github.com/dgiagio/warp#quickstart-with-net-core)

## Google Drive
For Google Drive scanning you need to get a Google Drive API credentials file, it's free!

1. Go to https://console.cloud.google.com/projectcreate
2. Fill in Project Name
3. Change Project ID (optional)
4. Wait a couple of seconds until the project is created and open it
5. Click "Go to APIs overview" in the APIs panel
6. Click "ENABLE APIS AND SERVICES"
7. Enter "Drive", select "Google Drive API"
8. Click "ENABLE"
9. Click "CREATE CREDENTIALS" in the tooltip
10. First box pick "Google Drive API"
11. Second box pick "Other UI (e.g. Windows, CLI tool)"
12. Select "Application data"
13. Click "What credentials do I need?"
14. Enter a "Service account name" (for example "serviceaccount")
15. JSON option is good
16. Click "Continue"
17. A dialog pops up, choose "CREATE WITHOUT A ROLE"
17. The needed Json file is downloaded
18. Rename this file to "OpenDirectoryDownloader.GoogleDrive.json" and place it in the OpenDirectoryDownloader.Google project, or place it in the same directory as the executable

## Contact me

Reddit https://www.reddit.com/user/KoalaBear84
