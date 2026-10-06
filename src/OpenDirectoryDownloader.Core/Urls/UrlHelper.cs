using OpenDirectoryDownloader.Helpers;
using System.Net;
using System.Text;

namespace OpenDirectoryDownloader.Urls;

public static class UrlHelper
{
	public static bool IsBase64String(string base64)
	{
		if (string.IsNullOrEmpty(base64))
		{
			return base64 is not null;
		}

		byte[] buffer = new byte[base64.Length];

		return Convert.TryFromBase64String(base64, buffer, out _);
	}

	/// <summary>Normalizes a URL typed by the user: decodes base64, adds a scheme and a trailing slash where it is a directory.</summary>
	public static string FixUrl(string url)
	{
		url = url.Trim();

		if (IsBase64String(url))
		{
			byte[] data = Convert.FromBase64String(url);
			url = Encoding.UTF8.GetString(data);
		}

		if (!url.Contains("http:") && !url.Contains("https:") && !url.Contains("ftp:") && !url.Contains("ftps:"))
		{
			url = $"http://{url}";
		}

		Uri uri = new(url);

		if (!url.EndsWith('/') && string.IsNullOrWhiteSpace(Path.GetFileName(WebUtility.UrlDecode(uri.AbsolutePath))) && string.IsNullOrWhiteSpace(uri.Query))
		{
			url += "/";
		}

		if (uri.Host == Constants.GoogleDriveDomain)
		{
			UrlEncodingParser urlEncodingParser = new(url);

			if (urlEncodingParser.AllKeys.Contains("usp"))
			{
				urlEncodingParser.Remove("usp");
			}

			url = urlEncodingParser.ToString();
		}

		return url;
	}

	/// <summary>The scan's output file name for a root URL.</summary>
	public static string CleanUriToFilename(Uri uri)
	{
		return PathHelper.GetValidPath(Uri.UnescapeDataString(uri.ToString()));
	}

	public static bool GetUriCredentials(Uri uri, out string username, out string password)
	{
		username = null;
		password = null;

		if (uri.UserInfo?.Contains(':') != true)
		{
			return false;
		}

		string[] splitted = uri.UserInfo.Split(':');

		username = WebUtility.UrlDecode(splitted.First());
		password = WebUtility.UrlDecode(splitted.Last());

		return true;
	}

	public static DateTime UnixTimestampToDateTime(long unixTimeStamp)
	{
		return DateTime.UnixEpoch.AddSeconds(unixTimeStamp);
	}

	public static string FormatWithThousands(object value)
	{
		return $"{value:#,0}";
	}

	// A listing resolves every line against the same base URL: parse it once per thread, not once per line
	[ThreadStatic]
	private static string _lastBaseUrl;

	[ThreadStatic]
	private static Uri _lastBaseUri;

	/// <summary>Resolves a link's href against the page URL.</summary>
	public static void ProcessUrl(string baseUrl, string href, out string linkHref, out Uri uri, out string fullUrl)
	{
		if (!ReferenceEquals(_lastBaseUrl, baseUrl) && _lastBaseUrl != baseUrl)
		{
			_lastBaseUri = new Uri(baseUrl);
			_lastBaseUrl = baseUrl;
		}

		linkHref = href;
		uri = new Uri(_lastBaseUri, linkHref);
		fullUrl = uri.ToString();
	}

	/// <summary>
	/// The file name of a listing entry, for the Uri ProcessUrl made and its fullUrl (always uri.ToString()).
	/// This was <c>Path.GetFileName(WebUtility.UrlDecode(new Uri(fullUrl).AbsolutePath))</c>: a second parse of the
	/// same URL for every file. The path of the Uri we already have is the same, except when it holds percent
	/// escapes: ToString() unescapes some of them, so re-parsing can escape them differently (%78 and %x, say).
	/// Those keep the old way.
	/// </summary>
	public static string GetFileNameFromUrl(Uri uri, string fullUrl)
	{
		string path = uri.AbsolutePath;

		if (path.Contains('%'))
		{
			path = new Uri(fullUrl).AbsolutePath;
		}

		return Path.GetFileName(WebUtility.UrlDecode(path));
	}

	/// <summary>The URL of the directory a URL is in.</summary>
	public static Uri GetUrlDirectory(string url)
	{
		return new Uri(new Uri(url), ".");
	}
}
