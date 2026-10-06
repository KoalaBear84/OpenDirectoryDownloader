using OpenDirectoryDownloader.Urls;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace OpenDirectoryDownloader.Cli;

/// <summary>
/// The command line, on System.CommandLine: every option keeps its name, short boolean options can be combined
/// like on Linux (-qcj is -q -c -j), and --http-cloak can be given with or without a preset.
/// </summary>
public static class CliDefinition
{
	public static readonly Option<string> Url = new("--url", "-u") { Description = "Url to scan" };
	public static readonly Option<int> Threads = new("--threads", "-t") { Description = "Number of threads", DefaultValueFactory = _ => 5 };
	public static readonly Option<int> Timeout = new("--timeout", "-o") { Description = "Number of seconds for timeout", DefaultValueFactory = _ => 100 };
	public static readonly Option<int> Wait = new("--wait", "-w") { Description = "Number of seconds to wait between calls (when single threaded is too fast..)" };
	public static readonly Option<bool> Quit = new("--quit", "-q") { Description = "Do not wait after scanning" };
	public static readonly Option<bool> Clipboard = new("--clipboard", "-c") { Description = "Copy Reddit stats after scanning" };
	public static readonly Option<bool> Json = new("--json", "-j") { Description = "Save JSON file" };
	public static readonly Option<bool> NoUrls = new("--no-urls", "-f") { Description = "Do not save URLs file" };
	public static readonly Option<bool> Aria2Urls = new("--aria2-urls", "-i") { Description = "Save aria2 urls files (with directory support)" };
	public static readonly Option<string> Aria2RootDir = new("--aria2-root-dir") { Description = "Specify a root directory for aria2 urls files (with directory support) e.g. /dir/" };
	public static readonly Option<bool> Aria2UrlDir = new("--aria2-url-dir") { Description = "Use URL as folder name for aria2 urls files (with directory support)" };
	public static readonly Option<bool> NoBrowser = new("--no-browser") { Description = "Do not launch browser (for cloudflare etc.)" };
	public static readonly Option<bool> NoReddit = new("--no-reddit", "-r") { Description = "Do not show Reddit stats markdown" };
	public static readonly Option<bool> ExactFileSizes = new("--exact-file-sizes", "-e") { Description = "Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)" };
	public static readonly Option<bool> UploadUrls = new("--upload-urls", "-l") { Description = "Uploads urls file" };
	public static readonly Option<bool> Speedtest = new("--speedtest", "-s") { Description = "Do a speed test" };
	public static readonly Option<string> UserAgent = new("--user-agent", "-a") { Description = "Use custom default User Agent" };
	public static readonly Option<string> Username = new("--username") { Description = "Username" };
	public static readonly Option<string> Password = new("--password") { Description = "Password" };
	public static readonly Option<string> GitHubToken = new("--github-token") { Description = "GitHub token" };
	public static readonly Option<string> OutputFile = new("--output-file") { Description = "Save output files to specific base filename" };
	public static readonly Option<bool> FastScan = new("--fast-scan") { Description = "Only perform actions that are fast, so no HEAD requests, etc. Might result in missing file sizes" };
	public static readonly Option<string> ProxyAddress = new("--proxy-address") { Description = "Proxy address, like: socks5://127.0.0.1:9050" };
	public static readonly Option<string> ProxyUsername = new("--proxy-username") { Description = "Proxy username" };
	public static readonly Option<string> ProxyPassword = new("--proxy-password") { Description = "Proxy password" };

	public const string DefaultHttpCloakPreset = "chrome-latest";

	public static readonly Option<string> HttpCloak = new("--http-cloak")
	{
		Description = "Use HttpCloak to emulate a real browser's TLS/HTTP fingerprint for improved compatibility. Use on its own to default to 'chrome-latest', or specify a preset, e.g. --http-cloak firefox-latest (other presets: safari-latest, chrome-latest-windows, etc). Omit entirely to disable. Note: bypasses this app's SSL certificate and automatic decompression, is a lot slower than without (its native proxy effectively serializes requests, regardless of --threads), and has no native binary for linux-arm.",
		Arity = ArgumentArity.ZeroOrOne,
		CustomParser = result => result.Tokens.Count == 0 ? DefaultHttpCloakPreset : result.Tokens[0].Value
	};

	public static readonly Option<string> FlareSolverrUrl = new("--flaresolverr-url") { Description = "FlareSolverr endpoint URL, e.g. http://127.0.0.1:8191" };
	public static readonly Option<string> FlareSolverrDockerName = new("--flaresolverr-docker-name") { Description = "FlareSolverr Docker container name to stream logs from, e.g. flaresolverr" };

	public static readonly Option<string[]> Header = new("--header", "-H")
	{
		Description = "Provide a custom header to use for any HTTP request while indexing. Option can be used multiple times for multiple headers.",
		Arity = ArgumentArity.ZeroOrMore,
		AllowMultipleArgumentsPerToken = false
	};

	public static readonly Option<bool> UseDatabase = new("--use-database") { Description = "Keep the scan's SQLite database (every scan uses one) next to the other output files, or at --db-path." };
	public static readonly Option<string> DbPath = new("--db-path") { Description = "Path for the SQLite scan database. Defaults to the output base filename with a .sqlite extension." };
	public static readonly Option<bool> KeepDb = new("--keep-db") { Description = "Keep the SQLite scan database after a successful scan instead of deleting it." };
	public static readonly Option<bool> EvictMemory = new("--evict-memory") { Description = "Accepted for compatibility: finished directory subtrees are always dropped from memory and read back from the scan database for output." };
	public static readonly Option<bool> Resume = new("--resume") { Description = "Continue a previously interrupted scan from the database at --db-path (or the default derived path), instead of starting over. Keeps the database file regardless of --keep-db, so an interrupted resume can itself be resumed again. If no existing database is found, starts a fresh scan as normal." };
	public static readonly Option<bool> RetryErrors = new("--retry-errors") { Description = "When resuming with --resume, retry directories that errored during the previous run instead of leaving them as-is. Without this, if any are found and the console is interactive, you'll be asked; otherwise they're left alone." };
	public static readonly Option<bool> Serve = new("--serve") { Description = "Start a local web server to browse a scan database (see 'Web viewer' in the README) instead of scanning a URL." };
	public static readonly Option<string> ServeDb = new("--serve-db") { Description = "Path to a .sqlite scan database to preload when using --serve. Optional - a database can also be dropped onto the page." };
	public static readonly Option<int> ServePort = new("--serve-port") { Description = "Port for --serve.", DefaultValueFactory = _ => 8080 };
	public static readonly Option<string> ServeHost = new("--serve-host") { Description = "Host/IP to bind --serve to. Defaults to loopback only; the server has no authentication, so only change this on a trusted network.", DefaultValueFactory = _ => "127.0.0.1" };
	public static readonly Option<bool> ServeNoBrowser = new("--serve-no-browser") { Description = "Don't automatically open the default browser when --serve starts." };

	/// <summary>A scan database dropped onto the executable starts the viewer with it</summary>
	public static readonly Argument<string> Database = new("database")
	{
		Description = "A .sqlite scan database to open in the web viewer",
		Arity = ArgumentArity.ZeroOrOne,
		Hidden = true
	};

	/// <summary>Built once: the options are shared and the validators must only be added once.</summary>
	public static RootCommand Command { get; } = Create();

	private static RootCommand Create()
	{
		RootCommand rootCommand = new("Indexes open directories (HTTP, FTP, Google Drive and many web front ends) and saves the URLs of the files.")
		{
			Url, Threads, Timeout, Wait, Quit, Clipboard, Json, NoUrls, Aria2Urls, Aria2RootDir, Aria2UrlDir, NoBrowser, NoReddit,
			ExactFileSizes, UploadUrls, Speedtest, UserAgent, Username, Password, GitHubToken, OutputFile, FastScan,
			ProxyAddress, ProxyUsername, ProxyPassword, HttpCloak, FlareSolverrUrl, FlareSolverrDockerName, Header,
			UseDatabase, DbPath, KeepDb, EvictMemory, Resume, RetryErrors, Serve, ServeDb, ServePort, ServeHost, ServeNoBrowser,
			Database
		};

		Threads.Validators.Add(result =>
		{
			if (result.GetValueOrDefault<int>() is < 1 or > 100)
			{
				result.AddError("Threads must be between 1 and 100");
			}
		});

		Database.Validators.Add(result =>
		{
			string path = result.GetValueOrDefault<string>();

			if (path is not null && (!File.Exists(path) || !Path.GetExtension(path).Equals(".sqlite", StringComparison.OrdinalIgnoreCase)))
			{
				result.AddError($"Unrecognized command or argument '{path}'.");
			}
		});

		rootCommand.Validators.Add(result =>
		{
			if (result.GetValue(Wait) > 0 && result.GetValue(Threads) > 1)
			{
				result.AddError("Using a wait time with more than 1 thread isn't supported, as it would still have multiple threads running.");
			}

			if (result.GetValue(ServeDb) is not null && !result.GetValue(Serve))
			{
				result.AddError("--serve-db can only be used with --serve.");
			}
		});

		return rootCommand;
	}

	public static ParseResult Parse(IReadOnlyList<string> args)
	{
		return Command.Parse(args, new ParserConfiguration
		{
			// A URL starting with @ is a URL, not a response file
			ResponseFileTokenReplacer = null
		});
	}

	/// <summary>
	/// Turns a successfully parsed command line into options. Credentials in the URL (user:password@) are used when
	/// none were given separately. The URL itself is only normalized here, asking for it is up to the caller.
	/// </summary>
	public static CliInvocation Bind(ParseResult parseResult)
	{
		string database = parseResult.GetValue(Database);
		string url = parseResult.GetValue(Url)?.Trim();

		RunMode mode = parseResult.GetValue(Serve) || database is not null ? RunMode.Serve :
			!string.IsNullOrWhiteSpace(url) && File.Exists(url) ? RunMode.Report :
			RunMode.Scan;

		string outputFile = parseResult.GetValue(OutputFile);

		Credentials credentials = new()
		{
			Username = parseResult.GetValue(Username),
			Password = parseResult.GetValue(Password),
			ProxyUsername = parseResult.GetValue(ProxyUsername),
			ProxyPassword = parseResult.GetValue(ProxyPassword),
			GitHubToken = parseResult.GetValue(GitHubToken)
		};

		return new CliInvocation(
			mode,
			new CrawlOptions
			{
				Url = url,
				Threads = parseResult.GetValue(Threads),
				TimeoutSeconds = parseResult.GetValue(Timeout),
				WaitSecondsBetweenCalls = parseResult.GetValue(Wait),
				ExactFileSizes = parseResult.GetValue(ExactFileSizes),
				FastScan = parseResult.GetValue(FastScan),
				NoBrowser = parseResult.GetValue(NoBrowser)
			},
			new HttpOptions
			{
				UserAgent = parseResult.GetValue(UserAgent),
				Headers = parseResult.GetValue(Header) ?? [],
				ProxyAddress = parseResult.GetValue(ProxyAddress),
				HttpCloakPreset = parseResult.GetValue(HttpCloak),
				FlareSolverrUrl = parseResult.GetValue(FlareSolverrUrl),
				FlareSolverrDockerName = parseResult.GetValue(FlareSolverrDockerName)
			},
			new OutputOptions
			{
				Quit = parseResult.GetValue(Quit),
				Clipboard = parseResult.GetValue(Clipboard),
				Json = parseResult.GetValue(Json),
				NoUrls = parseResult.GetValue(NoUrls),
				Aria2UrlsFile = parseResult.GetValue(Aria2Urls),
				Aria2RootDir = parseResult.GetValue(Aria2RootDir) ?? string.Empty,
				Aria2UrlDir = parseResult.GetValue(Aria2UrlDir),
				NoReddit = parseResult.GetValue(NoReddit),
				UploadUrls = parseResult.GetValue(UploadUrls),
				Speedtest = parseResult.GetValue(Speedtest),
				// Translates . and .. etc
				OutputFile = outputFile is null ? null : Path.GetFullPath(outputFile)
			},
			new StorageOptions
			{
				UseDatabase = parseResult.GetValue(UseDatabase),
				DbPath = parseResult.GetValue(DbPath),
				KeepDb = parseResult.GetValue(KeepDb),
				EvictMemory = parseResult.GetValue(EvictMemory),
				Resume = parseResult.GetValue(Resume),
				RetryErrors = parseResult.GetValue(RetryErrors)
			},
			new ServeOptions
			{
				Database = database ?? parseResult.GetValue(ServeDb),
				Port = parseResult.GetValue(ServePort),
				Host = parseResult.GetValue(ServeHost),
				NoBrowser = parseResult.GetValue(ServeNoBrowser)
			},
			credentials);
	}

	/// <summary>Uses the user:password in the URL when no credentials were given separately.</summary>
	public static Credentials WithUrlCredentials(Credentials credentials, string url)
	{
		if (credentials.HasBasicAuth || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || !UrlHelper.GetUriCredentials(uri, out string username, out string password))
		{
			return credentials;
		}

		return credentials with { Username = username, Password = password };
	}
}
