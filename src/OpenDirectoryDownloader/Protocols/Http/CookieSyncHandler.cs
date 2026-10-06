using Microsoft.Extensions.Logging;
using System.Net;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>
/// Replicates SocketsHttpHandler's CookieContainer behavior (attach outgoing Cookie header, capture incoming Set-Cookie headers)
/// for handlers that don't support CookieContainer themselves, like HttpCloakHandler.
///
/// Also undoes HttpCloakHandler's internal https-to-http rewrite of the request URI (it routes requests through a local
/// proxy this way, tagging the real scheme in the X-HTTPCloak-Scheme header). Without this, code reading
/// HttpResponseMessage.RequestMessage.RequestUri (e.g. to detect redirects or determine the effective URL) would
/// mistake every HttpCloak'd https request for one redirected to plain http.
/// </summary>
internal class CookieSyncHandler : DelegatingHandler
{
	private const string HttpCloakSchemeHeader = "X-HTTPCloak-Scheme";

	private readonly CookieContainer CookieContainer;
	private readonly ILogger Logger;

	public CookieSyncHandler(HttpMessageHandler innerHandler, CookieContainer cookieContainer, ILogger logger) : base(innerHandler)
	{
		CookieContainer = cookieContainer;
		Logger = logger;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string cookieHeader = CookieContainer.GetCookieHeader(request.RequestUri);

		if (!string.IsNullOrWhiteSpace(cookieHeader))
		{
			request.Headers.Remove("Cookie");
			request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
		}

		HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

		RestoreCloakedScheme(response);

		if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> setCookieHeaders))
		{
			foreach (string setCookieHeader in setCookieHeaders)
			{
				try
				{
					CookieContainer.SetCookies(request.RequestUri, setCookieHeader);
				}
				catch (CookieException ex)
				{
					Logger.LogDebug(ex, "Ignoring malformed Set-Cookie header '{header}' from {url}", setCookieHeader, request.RequestUri);
				}
			}
		}

		return response;
	}

	private static void RestoreCloakedScheme(HttpResponseMessage response)
	{
		HttpRequestMessage requestMessage = response.RequestMessage;
		Uri requestUri = requestMessage?.RequestUri;

		if (requestUri?.Scheme != Uri.UriSchemeHttp || !requestMessage.Headers.TryGetValues(HttpCloakSchemeHeader, out IEnumerable<string> schemeHeaderValues))
		{
			return;
		}

		string realScheme = schemeHeaderValues.FirstOrDefault();

		if (string.IsNullOrEmpty(realScheme) || realScheme == requestUri.Scheme)
		{
			return;
		}

		UriBuilder uriBuilder = new(requestUri) { Scheme = realScheme };

		if (requestUri.IsDefaultPort)
		{
			uriBuilder.Port = -1;
		}

		requestMessage.RequestUri = uriBuilder.Uri;
	}
}
