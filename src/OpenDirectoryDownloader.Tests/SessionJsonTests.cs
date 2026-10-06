using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Outputs;
using OpenDirectoryDownloader.Scanning;
using OpenDirectoryDownloader.Storage;
using System.Text.Json;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>The saved session (format 2): written streaming from the scan database, read back streaming.</summary>
public sealed class SessionJsonTests : IAsyncLifetime
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"odd-session-json-{Guid.NewGuid():N}");
	private ScanDatabase _scanDatabase;

	private string DatabasePath => Path.Combine(_directory, "scan.sqlite");

	private string JsonPath => Path.Combine(_directory, "scan.json");

	public async ValueTask InitializeAsync()
	{
		Directory.CreateDirectory(_directory);
		_scanDatabase = await ScanDatabase.CreateAsync(DatabasePath);
	}

	public async ValueTask DisposeAsync()
	{
		await _scanDatabase.DisposeAsync();

		try
		{
			Directory.Delete(_directory, recursive: true);
		}
		catch (IOException)
		{
			// Best effort
		}
	}

	private SessionJsonFactory CreateFactory(ScanSession session, CliInvocation invocation = null)
	{
		invocation ??= CliDefinition.Bind(CliDefinition.Parse(["--url", session.Root.Url, "--output-file", Path.Combine(_directory, "scan")]));

		return new SessionJsonFactory(session, _scanDatabase, invocation, new OutputPaths(session, invocation.Output), Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionJsonFactory>.Instance);
	}

	[Fact]
	public async Task UrlsWithErrors_RoundTripsAsJsonArray()
	{
		ScanSession session = new(new WebDirectory(null) { Url = "https://example.com/", Name = "root" });
		session.UrlsWithErrors.Add("https://example.com/a/");
		session.UrlsWithErrors.Add("https://example.com/b/");
		session.UrlsWithErrors.Add("https://example.com/a/");
		_scanDatabase.MirrorDirectory(session.Root);

		await CreateFactory(session).SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

		using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(JsonPath, TestContext.Current.CancellationToken)))
		{
			JsonElement urlsWithErrors = document.RootElement.GetProperty("urlsWithErrors");
			Assert.Equal(JsonValueKind.Array, urlsWithErrors.ValueKind);
			Assert.Equal(2, urlsWithErrors.GetArrayLength());
		}

		SessionSummary summary = SessionJsonReader.ReadSummary(JsonPath);
		Assert.Equal(2, summary.Header.UrlsWithErrors.Count);
		Assert.Contains("https://example.com/b/", summary.Header.UrlsWithErrors);
	}

	[Fact]
	public async Task Tree_RoundTripsTotalsAndExtensions()
	{
		ScanSession session = new(new WebDirectory(null) { Url = "https://example.com/", Name = "root", Finished = true });
		WebDirectory sub = new(session.Root) { Url = "https://example.com/sub/", Name = "sub", Finished = true };

		_scanDatabase.MirrorDirectory(session.Root);
		_scanDatabase.MirrorDirectory(sub);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/a.txt", FileName = "a.txt", FileSize = 10 }, session.Root);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://example.com/sub/b.TXT", FileName = "b.TXT", FileSize = 20 }, sub);
		_scanDatabase.MirrorFile(new WebFile { Url = "https://other.example.com/c.zip", FileName = "c.zip" }, sub);

		session.HttpStatusCodes.Add(200, 2);
		session.Counters.Restore(httpRequests: 2);

		await CreateFactory(session).SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

		SessionSummary summary = SessionJsonReader.ReadSummary(JsonPath);

		Assert.Equal("https://example.com/", summary.Header.Scan.RootUrl);
		Assert.Equal(3, summary.TotalFiles);
		Assert.Equal(30, summary.TotalFileSize);
		Assert.Equal(1, summary.TotalDirectories);
		Assert.Equal(2, summary.Extensions[".txt"].Count);
		Assert.Equal(1, summary.Extensions[".zip"].Count);
		Assert.Equal(2, summary.Header.Stats.HttpStatusCodes[200]);
		Assert.Equal(2, summary.Header.Stats.HttpRequests);

		// A file below its directory is stored relative to it, but the JSON has full URLs
		string json = await File.ReadAllTextAsync(JsonPath, TestContext.Current.CancellationToken);
		Assert.Contains("\"https://example.com/sub/b.TXT\"", json);
		Assert.Contains("\"https://other.example.com/c.zip\"", json);
	}

	[Fact]
	public async Task VeryDeepTree_IsWrittenAndReadWithoutDepthLimit()
	{
		ScanSession session = new(new WebDirectory(null) { Url = "https://example.com/", Name = "root", Finished = true });
		_scanDatabase.MirrorDirectory(session.Root);

		WebDirectory parent = session.Root;

		for (int level = 0; level < 2_000; level++)
		{
			WebDirectory directory = new(parent) { Url = $"{parent.Url}d/", Name = "d", Finished = true };
			_scanDatabase.MirrorDirectory(directory);
			parent = directory;
		}

		_scanDatabase.MirrorFile(new WebFile { Url = $"{parent.Url}deep.bin", FileName = "deep.bin", FileSize = 1 }, parent);

		await CreateFactory(session).SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

		SessionSummary summary = SessionJsonReader.ReadSummary(JsonPath);

		Assert.Equal(1, summary.TotalFiles);
		Assert.Equal(2_000, summary.TotalDirectories);
	}

	[Fact]
	public async Task Secrets_AreNotWritten()
	{
		ScanSession session = new(new WebDirectory(null) { Url = "https://example.com/", Name = "root" });
		_scanDatabase.MirrorDirectory(session.Root);

		CliInvocation invocation = CliDefinition.Bind(CliDefinition.Parse(["--url", "https://example.com/", "--password", "hunter2", "--github-token", "ghp_secret", "--proxy-password", "proxysecret", "-H", "Authorization: Bearer topsecret", "--output-file", Path.Combine(_directory, "scan")]));

		await CreateFactory(session, invocation).SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

		string json = await File.ReadAllTextAsync(JsonPath, TestContext.Current.CancellationToken);

		Assert.DoesNotContain("hunter2", json);
		Assert.DoesNotContain("ghp_secret", json);
		Assert.DoesNotContain("proxysecret", json);
		Assert.DoesNotContain("topsecret", json);
		Assert.Contains("Authorization", json);
	}
}
