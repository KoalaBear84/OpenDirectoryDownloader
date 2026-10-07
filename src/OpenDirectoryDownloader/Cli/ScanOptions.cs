namespace OpenDirectoryDownloader.Cli;

/// <summary>What the command line asks for: scan a URL, show a saved session, or serve a scan database.</summary>
public enum RunMode
{
	Scan,
	Report,
	Serve
}

/// <summary>The parsed command line, grouped by what it is about.</summary>
public sealed record CliInvocation(
	RunMode Mode,
	CrawlOptions Crawl,
	HttpOptions Http,
	OutputOptions Output,
	StorageOptions Storage,
	ServeOptions Serve,
	Credentials Credentials);

public sealed record CrawlOptions
{
	/// <summary>The URL as typed; null when it will be asked for</summary>
	public string Url { get; init; }

	public int Threads { get; init; } = 5;

	public int TimeoutSeconds { get; init; } = 100;

	public int WaitSecondsBetweenCalls { get; init; }

	public bool ExactFileSizes { get; init; }

	public bool FastScan { get; init; }

	public bool NoBrowser { get; init; }
}

public sealed record HttpOptions
{
	public string UserAgent { get; init; }

	/// <summary>-H values, "Name: value"</summary>
	public IReadOnlyList<string> Headers { get; init; } = [];

	public string ProxyAddress { get; init; }

	/// <summary>HttpCloak preset, null when not used</summary>
	public string HttpCloakPreset { get; init; }

	public string FlareSolverrUrl { get; init; }

	public string FlareSolverrDockerName { get; init; }
}

public sealed record OutputOptions
{
	public bool Quit { get; init; }

	public bool Clipboard { get; init; }

	public bool Json { get; init; }

	public bool NoUrls { get; init; }

	public bool Aria2UrlsFile { get; init; }

	public string Aria2RootDir { get; init; } = string.Empty;

	public bool Aria2UrlDir { get; init; }

	public bool NoReddit { get; init; }

	public bool UploadUrls { get; init; }

	public bool Speedtest { get; init; }

	/// <summary>Base path (without extension) for every output file, full path; null for Scans/&lt;url&gt;</summary>
	public string OutputFile { get; init; }
}

public sealed record StorageOptions
{
	public string DbPath { get; init; }

	public bool KeepDb { get; init; }

	public bool Resume { get; init; }

	public bool RetryErrors { get; init; }
}

public sealed record ServeOptions
{
	/// <summary>Scan database files and/or directories containing them</summary>
	public IReadOnlyList<string> Databases { get; init; } = [];

	public int Port { get; init; } = 8080;

	public string Host { get; init; } = "127.0.0.1";

	public bool NoBrowser { get; init; }
}

/// <summary>Secrets from the command line or the URL. Kept apart, so they are never written to a session or a log by accident.</summary>
public sealed record Credentials
{
	public string Username { get; init; }

	public string Password { get; init; }

	public string ProxyUsername { get; init; }

	public string ProxyPassword { get; init; }

	public string GitHubToken { get; init; }

	public bool HasBasicAuth => !string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password);

	public override string ToString() => $"Credentials {{ Username = {Username}, Password = {(string.IsNullOrEmpty(Password) ? "" : "***")} }}";
}
