using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Protocols.Http;

/// <summary>Reads response bodies as text, and tells whether one is HTML at all (or a file that only looked like a directory).</summary>
public static partial class BodyReader
{
	private const int SniffLength = 10 * Constants.Kilobyte;

	/// <summary>Charset name to encoding; only names the system knows (as listed by Encoding.GetEncodings), looked up once per name</summary>
	private static readonly ConcurrentDictionary<string, Encoding> Encodings = new(StringComparer.InvariantCultureIgnoreCase);

	private static readonly Lazy<HashSet<string>> KnownEncodingNames = new(() => new HashSet<string>(Encoding.GetEncodings().Select(e => e.Name), StringComparer.InvariantCultureIgnoreCase));

	/// <summary>
	/// The body as text when it is possibly HTML (empty for an empty body), else null: the first 10 KB must contain a
	/// tag and at most 10% control characters.
	/// </summary>
	/// <remarks>
	/// Decodes the body once, straight into the string (no copy per encoding step), and only after the first 10 KB
	/// showed it is worth it: a big file that turned out not to be a directory is never read as a whole.
	/// </remarks>
	public static async Task<string> ReadHtmlAsync(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken = default)
	{
		FixCharSet(httpResponseMessage);

		Encoding encoding = GetEncoding(httpResponseMessage.Content.Headers.ContentType?.CharSet);

		await using Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken);

		using StreamReader streamReader = new(stream, encoding);

		char[] buffer = ArrayPool<char>.Shared.Rent(SniffLength);

		try
		{
			int readChars = await streamReader.ReadBlockAsync(buffer.AsMemory(0, SniffLength), cancellationToken);

			if (readChars == 0)
			{
				return string.Empty;
			}

			ReadOnlySpan<char> start = buffer.AsSpan(0, readChars);

			if (!start.Contains('<') || !IsHtmlMaybe(start) || !HtmlRegex().IsMatch(start))
			{
				return null;
			}

			// The declared length is bytes, roughly the number of characters: a good size to start with, capped so a
			// bogus Content-Length can't make us reserve gigabytes up front
			long declaredLength = httpResponseMessage.Content.Headers.ContentLength ?? 0;
			StringBuilder builder = new(declaredLength > readChars ? (int)Math.Min(declaredLength, 64 * Constants.Megabyte) : readChars * 2);
			builder.Append(start);

			while ((readChars = await streamReader.ReadBlockAsync(buffer.AsMemory(0, SniffLength), cancellationToken)) > 0)
			{
				builder.Append(buffer, 0, readChars);
			}

			return builder.ToString();
		}
		finally
		{
			ArrayPool<char>.Shared.Return(buffer);
		}
	}

	/// <summary>The body as text, whatever it is.</summary>
	public static async Task<string> ReadTextAsync(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken = default)
	{
		FixCharSet(httpResponseMessage);

		return await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken);
	}

	/// <summary>At most 10% control characters (other than line feed, carriage return and tab), which should not be in HTML</summary>
	private static bool IsHtmlMaybe(ReadOnlySpan<char> text)
	{
		int controlChars = 0;

		foreach (char c in text)
		{
			if (char.IsControl(c) && c != 10 && c != 13 && c != 9)
			{
				controlChars++;
			}
		}

		return 100d / (text.Length / (double)controlChars) < 10;
	}

	private static Encoding GetEncoding(string charSet)
	{
		if (string.IsNullOrWhiteSpace(charSet))
		{
			return Encoding.UTF8;
		}

		return Encodings.GetOrAdd(charSet, static name => KnownEncodingNames.Value.Contains(name) ? Encoding.GetEncoding(name) : Encoding.UTF8);
	}

	/// <summary>Fixes some common wrong charset names</summary>
	public static void FixCharSet(HttpResponseMessage httpResponseMessage)
	{
		string charSet = httpResponseMessage.Content.Headers.ContentType?.CharSet;

		if (charSet is null)
		{
			return;
		}

		if (charSet.Equals("utf8", StringComparison.OrdinalIgnoreCase) || charSet.Equals("\"utf-8\"", StringComparison.OrdinalIgnoreCase) || charSet == "GB1212")
		{
			httpResponseMessage.Content.Headers.ContentType.CharSet = "UTF-8";
		}
		else if (charSet == "WIN-1251")
		{
			httpResponseMessage.Content.Headers.ContentType.CharSet = "Windows-1251";
		}
	}

	[GeneratedRegex("<[a-zA-Z0-9] ?([^>]+)>", RegexOptions.IgnoreCase, "en-US")]
	private static partial Regex HtmlRegex();
}
