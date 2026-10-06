using OpenDirectoryDownloader.Cli;
using System.CommandLine;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>The command line: every option keeps its name, short flags combine like on Linux.</summary>
public class CliTests
{
	private static CliInvocation Bind(params string[] args)
	{
		ParseResult parseResult = CliDefinition.Parse(args);

		Assert.True(parseResult.Errors.Count == 0, string.Join(", ", parseResult.Errors.Select(e => e.Message)));

		return CliDefinition.Bind(parseResult);
	}

	private static IReadOnlyList<string> Errors(params string[] args) => [.. CliDefinition.Parse(args).Errors.Select(e => e.Message)];

	[Fact]
	public void Defaults_AreTheDocumentedOnes()
	{
		CliInvocation invocation = Bind("-u", "http://example.com/");

		Assert.Equal(RunMode.Scan, invocation.Mode);
		Assert.Equal("http://example.com/", invocation.Crawl.Url);
		Assert.Equal(5, invocation.Crawl.Threads);
		Assert.Equal(100, invocation.Crawl.TimeoutSeconds);
		Assert.Equal(0, invocation.Crawl.WaitSecondsBetweenCalls);
		Assert.Equal(8080, invocation.Serve.Port);
		Assert.Equal("127.0.0.1", invocation.Serve.Host);
		Assert.Null(invocation.Http.HttpCloakPreset);
		Assert.Empty(invocation.Http.Headers);
		Assert.False(invocation.Output.Quit);
	}

	[Fact]
	public void ShortBooleanFlags_CanBeCombined()
	{
		CliInvocation invocation = Bind("-qcj", "-u", "http://example.com/");

		Assert.True(invocation.Output.Quit);
		Assert.True(invocation.Output.Clipboard);
		Assert.True(invocation.Output.Json);
		Assert.False(invocation.Output.NoUrls);
	}

	[Fact]
	public void AllShortBooleanFlags_CanBeCombined()
	{
		CliInvocation invocation = Bind("-qcjfirels", "-u", "http://example.com/");

		Assert.True(invocation.Output.Quit);
		Assert.True(invocation.Output.Clipboard);
		Assert.True(invocation.Output.Json);
		Assert.True(invocation.Output.NoUrls);
		Assert.True(invocation.Output.Aria2UrlsFile);
		Assert.True(invocation.Output.NoReddit);
		Assert.True(invocation.Crawl.ExactFileSizes);
		Assert.True(invocation.Output.UploadUrls);
		Assert.True(invocation.Output.Speedtest);
	}

	[Fact]
	public void EveryLongOption_StillExists()
	{
		CliInvocation invocation = Bind(
			"--url", "http://example.com/", "--threads", "7", "--timeout", "30", "--quit", "--clipboard", "--json", "--no-urls",
			"--aria2-urls", "--aria2-root-dir", "/dir/", "--aria2-url-dir", "--no-browser", "--no-reddit", "--exact-file-sizes",
			"--upload-urls", "--speedtest", "--user-agent", "agent", "--username", "user", "--password", "pass", "--github-token", "token",
			"--output-file", "out", "--fast-scan", "--proxy-address", "socks5://127.0.0.1:9050", "--proxy-username", "pu", "--proxy-password", "pp",
			"--flaresolverr-url", "http://127.0.0.1:8191", "--flaresolverr-docker-name", "flaresolverr", "--header", "X-A: 1",
			"--use-database", "--db-path", "db.sqlite", "--keep-db", "--evict-memory", "--resume", "--retry-errors",
			"--serve-port", "9000", "--serve-host", "0.0.0.0", "--serve-no-browser");

		Assert.Equal(7, invocation.Crawl.Threads);
		Assert.Equal(30, invocation.Crawl.TimeoutSeconds);
		Assert.True(invocation.Output.Quit && invocation.Output.Clipboard && invocation.Output.Json && invocation.Output.NoUrls);
		Assert.True(invocation.Output.Aria2UrlsFile && invocation.Output.Aria2UrlDir);
		Assert.Equal("/dir/", invocation.Output.Aria2RootDir);
		Assert.True(invocation.Crawl.NoBrowser && invocation.Output.NoReddit && invocation.Crawl.ExactFileSizes && invocation.Output.UploadUrls && invocation.Output.Speedtest && invocation.Crawl.FastScan);
		Assert.Equal("agent", invocation.Http.UserAgent);
		Assert.Equal("user", invocation.Credentials.Username);
		Assert.Equal("pass", invocation.Credentials.Password);
		Assert.Equal("token", invocation.Credentials.GitHubToken);
		Assert.Equal(Path.GetFullPath("out"), invocation.Output.OutputFile);
		Assert.Equal("socks5://127.0.0.1:9050", invocation.Http.ProxyAddress);
		Assert.Equal("pu", invocation.Credentials.ProxyUsername);
		Assert.Equal("pp", invocation.Credentials.ProxyPassword);
		Assert.Equal("http://127.0.0.1:8191", invocation.Http.FlareSolverrUrl);
		Assert.Equal("flaresolverr", invocation.Http.FlareSolverrDockerName);
		Assert.Equal(["X-A: 1"], invocation.Http.Headers);
		Assert.True(invocation.Storage.UseDatabase && invocation.Storage.KeepDb && invocation.Storage.EvictMemory && invocation.Storage.Resume && invocation.Storage.RetryErrors);
		Assert.Equal("db.sqlite", invocation.Storage.DbPath);
		Assert.Equal(9000, invocation.Serve.Port);
		Assert.Equal("0.0.0.0", invocation.Serve.Host);
		Assert.True(invocation.Serve.NoBrowser);
	}

	[Fact]
	public void ShortOptionsWithValues_StillWork()
	{
		CliInvocation invocation = Bind("-u", "http://example.com/", "-t", "1", "-o", "20", "-w", "3", "-a", "agent", "-H", "X-A: 1", "-H", "X-B: 2");

		Assert.Equal(1, invocation.Crawl.Threads);
		Assert.Equal(20, invocation.Crawl.TimeoutSeconds);
		Assert.Equal(3, invocation.Crawl.WaitSecondsBetweenCalls);
		Assert.Equal("agent", invocation.Http.UserAgent);
		Assert.Equal(["X-A: 1", "X-B: 2"], invocation.Http.Headers);
	}

	[Fact]
	public void HttpCloak_WithoutPreset_DefaultsAndDoesNotEatTheNextOption()
	{
		CliInvocation invocation = Bind("--http-cloak", "--url", "http://example.com/");

		Assert.Equal("chrome-latest", invocation.Http.HttpCloakPreset);
		Assert.Equal("http://example.com/", invocation.Crawl.Url);
	}

	[Theory]
	[InlineData("--http-cloak", "firefox-latest")]
	[InlineData("--http-cloak=firefox-latest")]
	public void HttpCloak_WithPreset(params string[] cloak)
	{
		CliInvocation invocation = Bind([.. cloak, "-u", "http://example.com/"]);

		Assert.Equal("firefox-latest", invocation.Http.HttpCloakPreset);
	}

	[Fact]
	public void HttpCloak_AsLastOption()
	{
		CliInvocation invocation = Bind("-u", "http://example.com/", "--http-cloak");

		Assert.Equal("chrome-latest", invocation.Http.HttpCloakPreset);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("101")]
	public void Threads_OutOfRange_IsAnError(string threads)
	{
		Assert.Contains("Threads must be between 1 and 100", Errors("-u", "http://example.com/", "-t", threads));
	}

	[Fact]
	public void Wait_WithMoreThanOneThread_IsAnError()
	{
		Assert.NotEmpty(Errors("-u", "http://example.com/", "-w", "2"));
		Assert.Empty(Errors("-u", "http://example.com/", "-w", "2", "-t", "1"));
	}

	[Fact]
	public void UnknownOption_IsAnError()
	{
		Assert.NotEmpty(Errors("-u", "http://example.com/", "--no-such-option"));
	}

	[Fact]
	public void AUrlStartingWithAt_IsNotAResponseFile()
	{
		CliInvocation invocation = Bind("-u", "@example.com");

		Assert.Equal("@example.com", invocation.Crawl.Url);
	}

	[Fact]
	public void Serve_IsServeMode()
	{
		CliInvocation invocation = Bind("--serve", "--serve-db", "scan.sqlite");

		Assert.Equal(RunMode.Serve, invocation.Mode);
		Assert.Equal("scan.sqlite", invocation.Serve.Database);
	}

	[Fact]
	public void ServeDb_WithoutServe_IsAnError()
	{
		Assert.NotEmpty(Errors("--serve-db", "scan.sqlite"));
	}

	[Fact]
	public void ALoneDatabaseFile_OpensTheViewer()
	{
		string path = Path.Combine(Path.GetTempPath(), $"odd-cli-{Guid.NewGuid():N}.sqlite");
		File.WriteAllBytes(path, []);

		try
		{
			CliInvocation invocation = Bind(path);

			Assert.Equal(RunMode.Serve, invocation.Mode);
			Assert.Equal(path, invocation.Serve.Database);
			Assert.Equal(8080, invocation.Serve.Port);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void APositionalArgumentThatIsNoDatabase_IsAnError()
	{
		Assert.NotEmpty(Errors("http://example.com/"));
	}

	[Fact]
	public void AnExistingFileAsUrl_IsReportMode()
	{
		string path = Path.Combine(Path.GetTempPath(), $"odd-cli-{Guid.NewGuid():N}.json");
		File.WriteAllText(path, "{}");

		try
		{
			Assert.Equal(RunMode.Report, Bind("-u", path).Mode);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void CredentialsInTheUrl_AreUsedWhenNoneAreGiven()
	{
		Credentials credentials = CliDefinition.WithUrlCredentials(new Credentials(), "http://user%40mail:p%3Ass@example.com/");

		Assert.Equal("user@mail", credentials.Username);
		Assert.Equal("p:ss", credentials.Password);
		Assert.Equal("given", CliDefinition.WithUrlCredentials(new Credentials { Username = "given" }, "http://user:pass@example.com/").Username);
	}

	[Fact]
	public void Credentials_AreNotInTheirText()
	{
		Assert.DoesNotContain("secret", new Credentials { Username = "user", Password = "secret", GitHubToken = "secret", ProxyPassword = "secret" }.ToString());
	}
}
