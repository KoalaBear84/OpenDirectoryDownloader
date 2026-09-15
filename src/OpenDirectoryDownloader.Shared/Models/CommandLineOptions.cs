using CommandLine;

namespace OpenDirectoryDownloader.Shared.Models;

public class CommandLineOptions
{
	[Option('u', "url", Required = false, HelpText = "Url to scan")]
	public string Url { get; set; }

	[Option('t', "threads", Required = false, Default = 5, HelpText = "Number of threads")]
	public int Threads { get; set; }

	[Option('o', "timeout", Required = false, Default = 100, HelpText = "Number of seconds for timeout")]
	public int Timeout { get; set; }

	[Option('w', "wait", Required = false, Default = 0, HelpText = "Number of seconds to wait between calls (when single threaded is too fast..)")]
	public int WaitSecondsBetweenCalls { get; set; }

	[Option('q', "quit", Required = false, Default = false, HelpText = "Do not wait after scanning")]
	public bool Quit { get; set; }

	[Option('c', "clipboard", Required = false, Default = false, HelpText = "Copy Reddit stats after scanning")]
	public bool Clipboard { get; set; }

	[Option('j', "json", Required = false, Default = false, HelpText = "Save JSON file")]
	public bool Json { get; set; }

	[Option('f', "no-urls", Required = false, Default = false, HelpText = "Do not save URLs file")]
	public bool NoUrls { get; set; }

	[Option('i', "aria2-urls", Required = false, Default = false, HelpText = "Save aria2 urls files (with directory support)")]
	public bool Aria2UrlsFile { get; set; }

	[Option("aria2-root-dir", Required = false, Default = "", HelpText = "Specify a root directory for aria2 urls files (with directory support) e.g. /dir/")]
	public string Aria2RootDir { get; set; }

	[Option("aria2-url-dir", Required = false, Default = false, HelpText = "Use URL as folder name for aria2 urls files (with directory support)")]
	public bool Aria2UrlDir { get; set; }

	[Option("no-browser", Required = false, Default = false, HelpText = "Do not launch browser (for cloudflare etc.)")]
	public bool NoBrowser { get; set; }

	[Option('r', "no-reddit", Required = false, Default = false, HelpText = "Do not show Reddit stats markdown")]
	public bool NoReddit { get; set; }

	[Option('e', "exact-file-sizes", Required = false, Default = false, HelpText = "Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)")]
	public bool ExactFileSizes { get; set; }

	[Option('l', "upload-urls", Required = false, Default = false, HelpText = "Uploads urls file")]
	public bool UploadUrls { get; set; }

	[Option('s', "speedtest", Required = false, Default = false, HelpText = "Do a speed test")]
	public bool Speedtest { get; set; }

	[Option('a', "user-agent", Required = false, HelpText = "Use custom default User Agent")]
	public string UserAgent { get; set; }

	[Option("username", Required = false, Default = "", HelpText = "Username")]
	public string Username { get; set; }

	[Option("password", Required = false, Default = "", HelpText = "Password")]
	public string Password { get; set; }

	[Option("github-token", Required = false, Default = "", HelpText = "GitHub token")]
	public string GitHubToken { get; set; }

	[Option("output-file", Required = false, Default = null, HelpText = "Save output files to specific base filename")]
	public string OutputFile { get; set; }

	[Option("fast-scan", Required = false, Default = false, HelpText = "Only perform actions that are fast, so no HEAD requests, etc. Might result in missing file sizes")]
	public bool FastScan { get; set; }

	[Option("proxy-address", Required = false, Default = "", HelpText = "Proxy address, like: socks5://127.0.0.1:9050")]
	public string ProxyAddress { get; set; }

	[Option("proxy-username", Required = false, Default = "", HelpText = "Proxy username")]
	public string ProxyUsername { get; set; }

	[Option("proxy-password", Required = false, Default = "", HelpText = "Proxy password")]
	public string ProxyPassword { get; set; }

	[Option("http-cloak", Required = false, Default = "", HelpText = "Use HttpCloak to emulate a real browser's TLS/HTTP fingerprint for improved compatibility. Use on its own to default to 'chrome-latest', or specify a preset, e.g. --http-cloak firefox-latest (other presets: safari-latest, chrome-latest-windows, etc). Omit entirely to disable. Note: bypasses this app's SSL certificate and automatic decompression, is a lot slower than without (its native proxy effectively serializes requests, regardless of --threads), and has no native binary for linux-arm.")]
	public string HttpCloak { get; set; }

	[Option("flaresolverr-url", Required = false, Default = "", HelpText = "FlareSolverr endpoint URL, e.g. http://127.0.0.1:8191")]
	public string FlareSolverrUrl { get; set; }

	[Option("flaresolverr-docker-name", Required = false, Default = "", HelpText = "FlareSolverr Docker container name to stream logs from, e.g. flaresolverr")]
	public string FlareSolverrDockerName { get; set; }

	[Option('H', "header", Required = false, Default = null, HelpText = "Provide a custom header to use for any HTTP request while indexing. Option can be used multiple times for multiple headers.")]
	public IEnumerable<string> Header { get; set; }

	[Option("use-database", Required = false, Default = false, HelpText = "EXPERIMENTAL: Mirror discovered directories and files to a SQLite database while scanning (issue #56, phase 1). Does not change how the scan works yet, only writes an additional, currently unused, working file.")]
	public bool UseDatabase { get; set; }

	[Option("db-path", Required = false, Default = null, HelpText = "Path for the SQLite database file used by --use-database. Defaults to the output base filename with a .sqlite extension.")]
	public string DbPath { get; set; }

	[Option("keep-db", Required = false, Default = false, HelpText = "Keep the SQLite database file (used by --use-database) after a successful scan instead of deleting it.")]
	public bool KeepDb { get; set; }

	[Option("evict-memory", Required = false, Default = false, HelpText = "EXPERIMENTAL: Requires --use-database. Drop a directory's in-memory files/subdirectories once its whole subtree has finished scanning, reading them back from the database for output instead. This is what actually reduces peak memory on very large scans (issue #56, phase 4); without it --use-database only mirrors to disk without freeing anything.")]
	public bool EvictMemory { get; set; }

	[Option("resume", Required = false, Default = false, HelpText = "EXPERIMENTAL: Continue a previously interrupted scan from the database at --db-path (or the default derived path), instead of starting over. Implies --use-database and keeps the database file regardless of --keep-db, so an interrupted resume can itself be resumed again. If no existing database is found, starts a fresh scan as normal.")]
	public bool Resume { get; set; }

	[Option("retry-errors", Required = false, Default = false, HelpText = "When resuming with --resume, retry directories that errored during the previous run instead of leaving them as-is. Without this, if any are found and the console is interactive, you'll be asked; otherwise they're left alone.")]
	public bool RetryErrors { get; set; }

	// TODO: Future use
	//[Option('d', "download", Required = false, HelpText = "Downloads the contents (after indexing is finished)")]
	//public bool Download { get; set; }
}
