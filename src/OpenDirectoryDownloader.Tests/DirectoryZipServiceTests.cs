using OpenDirectoryDownloader.Server;
using OpenDirectoryDownloader.Storage;
using System.IO.Compression;
using System.Net;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>
/// Regression coverage for the web viewer's directory-as-ZIP download (see 'Web viewer' in the README):
/// DirectoryZipService walks a ScanDatabase.SubtreeResult and fetches each file through an HttpClient, so
/// these tests stub the HttpClient instead of touching the network or a real database.
/// </summary>
public class DirectoryZipServiceTests
{
	private const string RootUrl = "https://example.com/folder/";

	private sealed class FakeHttpMessageHandler(Dictionary<string, byte[]> contentByUrl, HashSet<string> failingUrls = null) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri!.ToString();

			if (failingUrls is not null && failingUrls.Contains(url))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			HttpResponseMessage response = new(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(contentByUrl[url])
			};

			return Task.FromResult(response);
		}
	}

	[Fact]
	public async Task WriteZipAsync_BuildsArchiveMatchingFixtureTreeStructure()
	{
		List<ScanDatabase.MirroredDirectory> directories =
		[
			new ScanDatabase.MirroredDirectory($"{RootUrl}sub/", RootUrl, "sub", null, true, false)
		];

		List<ScanDatabase.SubtreeFile> files =
		[
			new ScanDatabase.SubtreeFile($"{RootUrl}root.txt", RootUrl, "root.txt", 5, null),
			new ScanDatabase.SubtreeFile($"{RootUrl}sub/nested.txt", $"{RootUrl}sub/", "nested.txt", 6, null)
		];

		ScanDatabase.SubtreeResult subtree = new(directories, files);

		Dictionary<string, byte[]> contentByUrl = new()
		{
			[$"{RootUrl}root.txt"] = "root!"u8.ToArray(),
			[$"{RootUrl}sub/nested.txt"] = "nested"u8.ToArray()
		};

		using HttpClient httpClient = new(new FakeHttpMessageHandler(contentByUrl));
		using MemoryStream destination = new();
		ZipJobProgress progress = new();

		await DirectoryZipService.WriteZipAsync(subtree, RootUrl, "folder", httpClient, destination, progress, CancellationToken.None);

		Assert.Equal(2, progress.TotalFiles);
		Assert.Equal(2, progress.FilesDone);
		Assert.Equal(0, progress.Skipped);

		destination.Position = 0;
		using ZipArchive archive = new(destination, ZipArchiveMode.Read);

		Assert.Equal(2, archive.Entries.Count);

		ZipArchiveEntry rootEntry = Assert.Single(archive.Entries, entry => entry.FullName == "folder/root.txt");
		using (StreamReader reader = new(rootEntry.Open()))
		{
			Assert.Equal("root!", await reader.ReadToEndAsync());
		}

		ZipArchiveEntry nestedEntry = Assert.Single(archive.Entries, entry => entry.FullName == "folder/sub/nested.txt");
		using (StreamReader reader = new(nestedEntry.Open()))
		{
			Assert.Equal("nested", await reader.ReadToEndAsync());
		}
	}

	[Fact]
	public async Task WriteZipAsync_FetchFailure_SkipsFileInsteadOfAbortingWholeZip()
	{
		ScanDatabase.SubtreeResult subtree = new(
			[],
			[
				new ScanDatabase.SubtreeFile($"{RootUrl}missing.txt", RootUrl, "missing.txt", 5, null),
				new ScanDatabase.SubtreeFile($"{RootUrl}ok.txt", RootUrl, "ok.txt", 2, null)
			]);

		Dictionary<string, byte[]> contentByUrl = new()
		{
			[$"{RootUrl}ok.txt"] = "ok"u8.ToArray()
		};

		HashSet<string> failingUrls = [$"{RootUrl}missing.txt"];

		using HttpClient httpClient = new(new FakeHttpMessageHandler(contentByUrl, failingUrls));
		using MemoryStream destination = new();
		ZipJobProgress progress = new();

		await DirectoryZipService.WriteZipAsync(subtree, RootUrl, "folder", httpClient, destination, progress, CancellationToken.None);

		Assert.Equal(2, progress.TotalFiles);
		Assert.Equal(2, progress.FilesDone);
		Assert.Equal(1, progress.Skipped);

		destination.Position = 0;
		using ZipArchive archive = new(destination, ZipArchiveMode.Read);

		ZipArchiveEntry entry = Assert.Single(archive.Entries);
		Assert.Equal("folder/ok.txt", entry.FullName);
	}

	[Fact]
	public async Task WriteZipAsync_MaliciousDirectoryNameCannotEscapeTheArchiveFolder()
	{
		List<ScanDatabase.MirroredDirectory> directories =
		[
			new ScanDatabase.MirroredDirectory($"{RootUrl}evil/", RootUrl, "..", null, true, false)
		];

		List<ScanDatabase.SubtreeFile> files =
		[
			new ScanDatabase.SubtreeFile($"{RootUrl}evil/payload.txt", $"{RootUrl}evil/", "payload.txt", 1, null)
		];

		ScanDatabase.SubtreeResult subtree = new(directories, files);

		Dictionary<string, byte[]> contentByUrl = new()
		{
			[$"{RootUrl}evil/payload.txt"] = "x"u8.ToArray()
		};

		using HttpClient httpClient = new(new FakeHttpMessageHandler(contentByUrl));
		using MemoryStream destination = new();
		ZipJobProgress progress = new();

		await DirectoryZipService.WriteZipAsync(subtree, RootUrl, "folder", httpClient, destination, progress, CancellationToken.None);

		destination.Position = 0;
		using ZipArchive archive = new(destination, ZipArchiveMode.Read);

		ZipArchiveEntry entry = Assert.Single(archive.Entries);
		Assert.StartsWith("folder/", entry.FullName);
		Assert.DoesNotContain("..", entry.FullName);
	}
}
