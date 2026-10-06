namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>
/// The request headers the crawler settles on during a scan: the user agent (from --user-agent, a working
/// fallback, or the browser after Cloudflare), the referer, and some extra Accept headers for picky servers.
/// Immutable: a change swaps the whole profile, so a request never sees half of one.
/// </summary>
public sealed record RequestProfile
{
	public string UserAgent { get; init; }

	public string Referer { get; init; }

	/// <summary>Added to Accept and Accept-Language, after the defaults</summary>
	public bool ExtraBrowserAcceptHeaders { get; init; }
}

/// <summary>The scan-wide HTTP state: the current <see cref="RequestProfile"/>, and whether the first directory (the root) succeeded yet.</summary>
public sealed class HttpScanState
{
	private RequestProfile _profile = new();
	private volatile bool _firstRequest = true;

	public RequestProfile Profile => Volatile.Read(ref _profile);

	/// <summary>True until the first directory was retrieved successfully: the root gets fallbacks the others don't.</summary>
	public bool FirstRequest
	{
		get => _firstRequest;
		set => _firstRequest = value;
	}

	public void Update(Func<RequestProfile, RequestProfile> change)
	{
		RequestProfile current;
		RequestProfile changed;

		do
		{
			current = Volatile.Read(ref _profile);
			changed = change(current);
		}
		while (Interlocked.CompareExchange(ref _profile, changed, current) != current);
	}
}

/// <summary>Adds the current <see cref="RequestProfile"/> to every request that does not set those headers itself (the GitHub API does).</summary>
internal sealed class RequestProfileHandler(HttpScanState state) : DelegatingHandler
{
	private const string AcceptLanguage = "en-US,en;q=0.9,nl;q=0.8";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		RequestProfile profile = state.Profile;

		if (profile.UserAgent is not null && request.Headers.UserAgent.Count == 0)
		{
			request.Headers.TryAddWithoutValidation("User-Agent", profile.UserAgent);
		}

		if (profile.Referer is not null && request.Headers.Referrer is null)
		{
			request.Headers.TryAddWithoutValidation("Referer", profile.Referer);
		}

		if (profile.ExtraBrowserAcceptHeaders)
		{
			request.Headers.TryAddWithoutValidation("Accept", "text/html");
			request.Headers.TryAddWithoutValidation("Accept-Language", AcceptLanguage);
		}

		return base.SendAsync(request, cancellationToken);
	}
}
