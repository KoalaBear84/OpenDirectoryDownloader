using FlareSolverrSharp;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using OpenDirectoryDownloader.Scanning;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>The scan's one HttpClient and what belongs with it: its cookies and its changing request headers.</summary>
public sealed partial class ScanHttp : IDisposable
{
	private readonly ScanSession _session;
	private readonly ILogger<ScanHttp> _logger;

	public ScanHttp(ScanSession session, CrawlOptions crawlOptions, HttpOptions httpOptions, Credentials credentials, ILogger<ScanHttp> logger)
	{
		_session = session;
		_logger = logger;

		SocketsHttpHandler socketsHttpHandler = new()
		{
			SslOptions = new SslClientAuthenticationOptions
			{
				RemoteCertificateValidationCallback = CheckCertificate
			},
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
			CookieContainer = Cookies,
			// Long scans: pick up DNS changes, don't keep a dead connection forever
			PooledConnectionLifetime = TimeSpan.FromMinutes(10),
			ConnectTimeout = TimeSpan.FromSeconds(Math.Min(crawlOptions.TimeoutSeconds, 30))
		};

		if (!string.IsNullOrWhiteSpace(httpOptions.ProxyAddress))
		{
			WebProxy webProxy = new()
			{
				Address = new Uri(httpOptions.ProxyAddress),
			};

			if (!string.IsNullOrWhiteSpace(credentials.ProxyUsername) || !string.IsNullOrWhiteSpace(credentials.ProxyPassword))
			{
				webProxy.Credentials = new NetworkCredential(credentials.ProxyUsername, credentials.ProxyPassword);
			}

			socketsHttpHandler.Proxy = webProxy;
		}

		HttpMessageHandler innerHandler = socketsHttpHandler;

		if (!string.IsNullOrWhiteSpace(httpOptions.HttpCloakPreset))
		{
			string cloakProxy = string.Empty;

			if (!string.IsNullOrWhiteSpace(httpOptions.ProxyAddress))
			{
				UriBuilder proxyUriBuilder = new(httpOptions.ProxyAddress);

				if (!string.IsNullOrWhiteSpace(credentials.ProxyUsername) || !string.IsNullOrWhiteSpace(credentials.ProxyPassword))
				{
					proxyUriBuilder.UserName = Uri.EscapeDataString(credentials.ProxyUsername ?? string.Empty);
					proxyUriBuilder.Password = Uri.EscapeDataString(credentials.ProxyPassword ?? string.Empty);
				}

				cloakProxy = proxyUriBuilder.Uri.ToString();
			}

			HttpCloak.HttpCloakHandler httpCloakHandler = new(preset: httpOptions.HttpCloakPreset, proxy: cloakProxy);
			innerHandler = new CookieSyncHandler(httpCloakHandler, Cookies, logger);
		}

		if (!string.IsNullOrWhiteSpace(httpOptions.FlareSolverrUrl))
		{
			if (!string.IsNullOrWhiteSpace(httpOptions.HttpCloakPreset))
			{
				logger.LogWarning("--http-cloak and --flaresolverr-url were both specified; they cannot be combined, FlareSolverr takes precedence");
			}

			logger.LogWarning("Using FlareSolverr endpoint: {flareSolverrUrl}", httpOptions.FlareSolverrUrl);
			innerHandler = new ClearanceHandler(httpOptions.FlareSolverrUrl)
			{
				InnerHandler = socketsHttpHandler
			};
		}

		if (!string.IsNullOrWhiteSpace(httpOptions.UserAgent))
		{
			State.Update(profile => profile with { UserAgent = httpOptions.UserAgent });
		}

		Client = new HttpClient(new RequestProfileHandler(State) { InnerHandler = innerHandler })
		{
			Timeout = TimeSpan.FromSeconds(crawlOptions.TimeoutSeconds)
		};

		// Fixed for the whole scan; what changes during a scan is in State.Profile
		Client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
		Client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
		Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,nl;q=0.8");

		AddCustomHeaders(httpOptions.Headers);

		if (credentials.HasBasicAuth)
		{
			Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}")));
		}
	}

	public HttpClient Client { get; }

	public CookieContainer Cookies { get; } = new();

	public HttpScanState State { get; } = new();

	private void AddCustomHeaders(IReadOnlyList<string> headers)
	{
		foreach (string customHeader in headers)
		{
			if (!customHeader.Contains(':'))
			{
				_logger.LogWarning("Invalid header specified: '{customHeader}' should contain the header name and value, separated by a colon (:). Header will be ignored.", customHeader);
				continue;
			}

			string[] splitHeader = customHeader.Split(':');

			string headerName = splitHeader[0].TrimStart();
			string headerValue = string.Join(":", splitHeader.Skip(1)).TrimStart();

			if (headerName.Equals("cookie", StringComparison.InvariantCultureIgnoreCase))
			{
				foreach (string cookie in headerValue.Split(';').Where(c => !string.IsNullOrWhiteSpace(c)))
				{
					string[] splitCookie = cookie.Split('=');

					if (splitCookie.Length != 2)
					{
						_logger.LogWarning("Invalid cookie found: '{cookie}' should contain a cookie name and value, separated by '='. Cookie will be ignored.", cookie);
						continue;
					}

					_logger.LogWarning("Adding cookie: name={name}, value={value}", splitCookie[0], splitCookie[1]);
					Cookies.Add(_session.Root.Uri, new Cookie(splitCookie[0].Trim(), splitCookie[1]));
				}
			}
			else
			{
				Client.DefaultRequestHeaders.Add(headerName, headerValue);
			}
		}
	}

	/// <summary>
	/// Accepts every certificate (open directories often have broken ones). On the first request, when the name does
	/// not match, it tells which host names the certificate is for: the right URL might be one of those.
	/// </summary>
	private bool CheckCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
	{
		if (!State.FirstRequest || !sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || certificate is null)
		{
			return true;
		}

		string url = _session.Root.Url;

		try
		{
			string urlHostname = new Uri(url).Host;
			List<string> possibleDnsNames = [.. CommonNameRegex().Matches(certificate.Subject).Select(m => m.Groups["CommonName"].Value)];

			if (certificate is X509Certificate2 x509Certificate2)
			{
				foreach (X509SubjectAlternativeNameExtension extension in x509Certificate2.Extensions.OfType<X509SubjectAlternativeNameExtension>())
				{
					AsnEncodedData asnEncodedData = new(extension.Oid, extension.RawData);
					possibleDnsNames.AddRange(DnsNameRegex().Matches(asnEncodedData.Format(true)).Select(m => m.Groups["DnsName"].Value));
				}
			}

			possibleDnsNames = [.. possibleDnsNames.Distinct()];

			if (!possibleDnsNames.Contains(urlHostname, StringComparer.OrdinalIgnoreCase) &&
				!possibleDnsNames.Any(dnsName => Regex.IsMatch(urlHostname, $"^{Regex.Escape(dnsName).Replace("\\?", ".").Replace("\\*", ".*")}$")))
			{
				foreach (string possibleDnsName in possibleDnsNames.Where(dnsName => dnsName.Contains('.') && Uri.CheckHostName(dnsName) != UriHostNameType.Unknown))
				{
					UriBuilder uriBuilder = new(url)
					{
						Host = possibleDnsName
					};

					if (_session.PossibleAlternativeUrls.Add(uriBuilder.Uri.ToString()))
					{
						_logger.LogWarning("Correct URL might be: {Url}", uriBuilder.Uri);
					}
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error checking SSL certificate host names for {Url} from {Subject}, please report!", url, certificate.Subject);
		}

		return true;
	}

	public void Dispose() => Client.Dispose();

	[GeneratedRegex("CN=(?<CommonName>[^,]+),.+")]
	private static partial Regex CommonNameRegex();

	[GeneratedRegex(@"DNS Name=(?<DnsName>\S*)")]
	private static partial Regex DnsNameRegex();
}
