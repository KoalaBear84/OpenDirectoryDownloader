using OpenDirectoryDownloader;
using OpenDirectoryDownloader.Shared.Models;
using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

public class TlsConfigurationTests
{
	/// <summary>
	/// Verify that TLS 1.2 and 1.3 are enabled in the SSL configuration
	/// </summary>
	[Fact]
	public void Test_TLS_Protocols_Are_Configured()
	{
		// Arrange
		var settings = new OpenDirectoryIndexerSettings
		{
			CommandLineOptions = new CommandLineOptions
			{
				Url = "https://example.com",
				Header = []
			},
			Url = "https://example.com",
			Threads = 1,
			Timeout = 30,
			Username = string.Empty,
			Password = string.Empty
		};

		// Act
		var indexer = new OpenDirectoryIndexer(settings);

		// Use reflection to access the private SocketsHttpHandler property
		var socketsHttpHandlerProperty = typeof(OpenDirectoryIndexer)
			.GetProperty("SocketsHttpHandler", BindingFlags.NonPublic | BindingFlags.Instance);
		
		Assert.NotNull(socketsHttpHandlerProperty);
		
		var socketsHttpHandler = socketsHttpHandlerProperty.GetValue(indexer) as System.Net.Http.SocketsHttpHandler;
		
		Assert.NotNull(socketsHttpHandler);
		Assert.NotNull(socketsHttpHandler.SslOptions);

		// Assert
		// Verify that both TLS 1.2 and TLS 1.3 are enabled
		var enabledProtocols = socketsHttpHandler.SslOptions.EnabledSslProtocols;
		
		Assert.True(enabledProtocols.HasFlag(SslProtocols.Tls12), "TLS 1.2 should be enabled");
		Assert.True(enabledProtocols.HasFlag(SslProtocols.Tls13), "TLS 1.3 should be enabled");
		
		// Verify that older insecure protocols are NOT enabled
		Assert.False(enabledProtocols.HasFlag(SslProtocols.Tls), "TLS 1.0 should not be enabled");
		Assert.False(enabledProtocols.HasFlag(SslProtocols.Tls11), "TLS 1.1 should not be enabled");
#pragma warning disable SYSLIB0039 // Type or member is obsolete
		Assert.False(enabledProtocols.HasFlag(SslProtocols.Ssl2), "SSL 2 should not be enabled");
		Assert.False(enabledProtocols.HasFlag(SslProtocols.Ssl3), "SSL 3 should not be enabled");
#pragma warning restore SYSLIB0039
	}
}
