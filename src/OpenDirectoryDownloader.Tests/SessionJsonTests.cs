using OpenDirectoryDownloader.Shared.Models;
using System.Text.Json;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class SessionJsonTests
{
	[Fact]
	public void UrlsWithErrors_RoundTripsAsJsonArray()
	{
		Session session = new() { Root = new WebDirectory(null) { Url = "https://example.com/", Name = "root" } };
		session.UrlsWithErrors.Add("https://example.com/a/");
		session.UrlsWithErrors.Add("https://example.com/b/");
		session.UrlsWithErrors.Add("https://example.com/a/");

		string json = JsonSerializer.Serialize(session);

		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement urlsWithErrors = document.RootElement.GetProperty("UrlsWithErrors");
		Assert.Equal(JsonValueKind.Array, urlsWithErrors.ValueKind);
		Assert.Equal(2, urlsWithErrors.GetArrayLength());

		Session roundTripped = JsonSerializer.Deserialize<Session>(json);
		Assert.Equal(2, roundTripped.UrlsWithErrors.Count);
		Assert.Contains("https://example.com/b/", roundTripped.UrlsWithErrors);
	}
}
