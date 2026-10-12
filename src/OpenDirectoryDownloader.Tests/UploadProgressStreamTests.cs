using OpenDirectoryDownloader.Outputs.FileUpload;
using Xunit;

namespace OpenDirectoryDownloader.Tests;

/// <summary>The upload progress wrapper must not change what is sent: same bytes, and still a known Content-Length</summary>
public class UploadProgressStreamTests
{
	[Fact]
	public async Task Upload_SendsAllBytesWithContentLength_AndReportsProgressUntil100()
	{
		byte[] data = new byte[3 * 1024 * 1024 + 123];
		Random.Shared.NextBytes(data);

		string path = Path.GetTempFileName();
		await File.WriteAllBytesAsync(path, data, TestContext.Current.CancellationToken);

		TextWriter originalOut = Console.Out;
		StringWriter output = new();
		Console.SetOut(output);

		try
		{
			using HttpClient client = new(new CaptureHandler());

			await using (FileStream fileStream = new(path, FileMode.Open, FileAccess.Read))
			{
				using StreamContent content = new(new UploadProgressStream(fileStream, "Test"));
				using HttpResponseMessage response = await client.PostAsync("http://localhost/upload", content, TestContext.Current.CancellationToken);

				Assert.Equal(data.Length, CaptureHandler.ContentLength);
				Assert.Equal(data.Length, CaptureHandler.BytesRead);
			}
		}
		finally
		{
			Console.SetOut(originalOut);
			File.Delete(path);
		}

		string text = output.ToString();
		Assert.Contains("Uploading to Test:", text);
		Assert.Contains("100% (3 MiB of 3 MiB", text);
		Assert.Single(text.Split("100%").Skip(1));
	}

	private sealed class CaptureHandler : HttpMessageHandler
	{
		public static long? ContentLength { get; private set; }
		public static long BytesRead { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			ContentLength = request.Content.Headers.ContentLength;

			using MemoryStream sink = new();
			await request.Content.CopyToAsync(sink, cancellationToken);
			BytesRead = sink.Length;

			return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
		}
	}
}
