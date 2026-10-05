using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace OpenDirectoryDownloader.Tests.Integration;

/// <summary>
/// A real HTTP server on a random loopback port that serves a <see cref="FakeSite"/> as Apache / nginx /
/// lighttpd style directory listings. No external services involved; dispose it when the test is done.
/// </summary>
public sealed class FakeDirectoryServer : IAsyncDisposable
{
	private readonly WebApplication _app;
	private long _directoryRequests;
	private long _fileRequests;
	private long _failedRequests;
	private long _inFlight;
	private long _peakInFlight;
	private long _handlerTicks;
	private long _handledRequests;

	public FakeSite Site { get; }

	/// <summary>Root URL to scan, e.g. http://127.0.0.1:51234/</summary>
	public string BaseUrl { get; private set; }

	public long DirectoryRequests => Interlocked.Read(ref _directoryRequests);

	public long FileRequests => Interlocked.Read(ref _fileRequests);

	public long FailedRequests => Interlocked.Read(ref _failedRequests);

	/// <summary>Most requests the server was handling at the same moment: shows whether the client actually kept it busy.</summary>
	public long PeakConcurrentRequests => Interlocked.Read(ref _peakInFlight);

	/// <summary>Average time the server spent on one request (generating and writing the response): if this is far below the time between requests, the client is the bottleneck.</summary>
	public double AverageHandlerMilliseconds => Interlocked.Read(ref _handledRequests) == 0 ? 0 : Interlocked.Read(ref _handlerTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency / Interlocked.Read(ref _handledRequests);

	private FakeDirectoryServer(FakeSite site)
	{
		Site = site;

		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");

		_app = builder.Build();
		_app.Run(HandleAsync);
	}

	public static async Task<FakeDirectoryServer> StartAsync(FakeSiteOptions options)
	{
		FakeDirectoryServer server = new(new FakeSite(options));
		await server._app.StartAsync();

		IServerAddressesFeature addresses = server._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
		server.BaseUrl = addresses.Addresses.First().Replace("[::]", "127.0.0.1").TrimEnd('/') + "/";

		return server;
	}

	private async Task HandleAsync(HttpContext context)
	{
		long inFlight = Interlocked.Increment(ref _inFlight);
		long peak;

		while (inFlight > (peak = Interlocked.Read(ref _peakInFlight)) && Interlocked.CompareExchange(ref _peakInFlight, inFlight, peak) != peak)
		{
		}

		long started = System.Diagnostics.Stopwatch.GetTimestamp();

		try
		{
			await HandleCoreAsync(context);
		}
		finally
		{
			Interlocked.Add(ref _handlerTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started);
			Interlocked.Increment(ref _handledRequests);
			Interlocked.Decrement(ref _inFlight);
		}
	}

	private async Task HandleCoreAsync(HttpContext context)
	{
		if (Site.Options.Latency > TimeSpan.Zero)
		{
			await Task.Delay(Site.Options.Latency, context.RequestAborted);
		}

		string path = WebUtility.UrlDecode(context.Request.Path.Value ?? "/");
		context.Response.Headers.Server = Site.Options.Style == ListingStyle.NginxAutoindex ? "nginx/1.18.0" : Site.Options.Style == ListingStyle.HtmlTable ? "lighttpd/1.4.59" : "Apache/2.4.41 (Ubuntu)";

		if (path.EndsWith('/'))
		{
			if (!Site.DirectoryExists(path))
			{
				context.Response.StatusCode = StatusCodes.Status404NotFound;
				return;
			}

			Interlocked.Increment(ref _directoryRequests);

			if (Site.Options.FailDirectory?.Invoke(path) == true)
			{
				Interlocked.Increment(ref _failedRequests);
				context.Response.StatusCode = StatusCodes.Status500InternalServerError;
				return;
			}

			byte[] body = Encoding.UTF8.GetBytes(Site.RenderListing(path));
			context.Response.ContentType = "text/html;charset=UTF-8";
			context.Response.ContentLength = body.Length;

			if (!HttpMethods.IsHead(context.Request.Method))
			{
				await context.Response.Body.WriteAsync(body, context.RequestAborted);
			}

			return;
		}

		// A file: answer HEAD with the right length (used by exact file size lookups), GET with a tiny body
		int separator = path.LastIndexOf('/');
		string directoryPath = path[..(separator + 1)];
		string fileName = path[(separator + 1)..];

		if (!Site.DirectoryExists(directoryPath))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		for (int i = 0; i < Site.FileCount(directoryPath); i++)
		{
			if (FakeSite.FileName(i) != fileName)
			{
				continue;
			}

			Interlocked.Increment(ref _fileRequests);
			context.Response.ContentType = "application/octet-stream";
			context.Response.ContentLength = FakeSite.FileSize(directoryPath, i);

			if (!HttpMethods.IsHead(context.Request.Method))
			{
				// Only a probe is ever read by the app; abort after a small chunk instead of streaming gigabytes
				await context.Response.Body.WriteAsync(new byte[1024], context.RequestAborted);
				context.Abort();
			}

			return;
		}

		context.Response.StatusCode = StatusCodes.Status404NotFound;
	}

	public async ValueTask DisposeAsync()
	{
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
