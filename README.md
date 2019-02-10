# Open Directory Downloader
Indexes open directories. Downloading is not (yet) implemented, but is possible when you use the resulting file into another tool.

If you have improvements supply me with a pull request! If you have a format not yet supported, please let me know.

## Usage

Command line paremters:

Short | Long | Description
---------|----------|---------
 `-u` | `--url` | Url to scan
 `-q` | `--quit` | Do not wait after scanning
 `-j` | `--json` | Save JSON file
 `-f` | `--no-urls` | Do not save URLs file
 `-r` | `--no-reddit` | Do not show Reddit stats markdown
 `-e` | `--exact-file-sizes` | Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)
 `-s` | `--speed-test` | Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)

 Example:
 `OpenDirectoryIndexer.exe --url "https://myopendirectory.com"`

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