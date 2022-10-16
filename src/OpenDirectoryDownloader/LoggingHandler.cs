namespace OpenDirectoryDownloader;

public class LoggingHandler : DelegatingHandler
{
	public LoggingHandler() : base()
	{

	}

	public LoggingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
	{
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Console.WriteLine(await request.ToRawString());

		HttpResponseMessage httpResponseMessage = await base.SendAsync(request, cancellationToken);

		Console.WriteLine(await httpResponseMessage.ToRawString());

		return httpResponseMessage;
	}
}
