using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenDirectoryDownloader.Helpers;

public static partial class FileSizeHelper
{
	// Parse a file size.
	private static readonly string[][] SizeSuffixes =
	[
		["BYTES", "BYTE", "B", "OCTETS", "OCTET"],
		["KB", "K", "KIB", "KO"],
		["MB", "M", "MIB", "MO"],
		["GB", "G", "GIB", "GO"],
		["TB", "T", "TIB", "TO"],
		["PB", "P", "PIB", "PO"],
		["EB", "E", "EIB", "EO"],
		["ZB", "Z", "ZIB", "ZO"],
		["YB", "Y", "YIB", "YO"]
	];

	private static readonly Regex AlphaNumericRegex = new("[^a-zA-Z0-9 .,]");
	private static readonly Regex BytesRegex = new("\\((?<FileSize>(\\d*,?)+)bytes\\)");

	public static long? ParseFileSize(string value, int kbValue = 1024, bool throwException = false, bool onlyChecking = false)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		// Strip HTML
		value = RegexStripHtml().Replace(value, string.Empty);

		Match bytesRegexMatch = BytesRegex.Match(value);

		if (bytesRegexMatch.Success)
		{
			return long.Parse(bytesRegexMatch.Groups["FileSize"].Value.Replace(",", string.Empty));
		}

		value = AlphaNumericRegex.Replace(value, string.Empty);

		// Remove leading and trailing spaces.
		value = value.Trim();

		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		try
		{
			// Find the last non-alphabetic character.
			int extStart = 0;

			for (int i = value.Length - 1; i >= 0; i--)
			{
				// Stop if we find something other than a letter.
				if (char.IsLetter(value, i))
				{
					continue;
				}

				extStart = i + 1;
				break;
			}

			// Get the numeric part.
			if (double.TryParse(value.AsSpan(0, extStart), NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
			{
				// Get the extension.
				string suffix;

				if (extStart < value.Length)
				{
					suffix = value[extStart..].Trim().ToUpperInvariant();
				}
				else
				{
					suffix = "BYTES";
				}

				// Find the extension in the list.
				int suffix_index = -1;

				for (int i = 0; i < SizeSuffixes.Length; i++)
				{
					if (!SizeSuffixes[i].ToList().Contains(suffix))
					{
						continue;
					}

					suffix_index = i;
					break;
				}

				if (suffix_index < 0)
				{
					throw new FormatException($"Unknown file size extension {suffix}, value: {value}.");
				}

				// Return the result.
				return (long)Math.Round(number * Math.Pow(kbValue, suffix_index));
			}

			return null;
		}
		catch (Exception ex)
		{
			if (throwException)
			{
				throw new FormatException($"Invalid file size format, value: {value}", ex);
			}

			if (!onlyChecking)
			{
				Program.Logger.Warning("Cannot parse '{value}' as a filesize.", value);
			}

			return null;
		}
	}

	private static readonly string[] sizeSuffixes = ["B", "kiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];
	private static readonly string[] sizeSuffixesBit = ["b", "kib", "Mib", "Gib", "Tib", "Pib", "Eib", "Zib", "Yib"];

	public static string ToHumanReadable(long? size, bool useBits = false)
	{
		Debug.Assert(sizeSuffixes.Length > 0);

		const string formatTemplate = "{0}{1:#.##} {2}";

		switch (size)
		{
			case null:
				return "-";
			case 0:
				return string.Format(formatTemplate, null, 0, sizeSuffixes[0]);
		}

		double absSize = Math.Abs((double)size);
		double fpPower = Math.Log(absSize, 1000);
		int intPower = (int)fpPower;
		int iUnit = intPower >= sizeSuffixes.Length ? sizeSuffixes.Length - 1 : intPower;
		double normSize = absSize / Math.Pow(1024, iUnit);

		return string.Format(formatTemplate, size < 0 ? "-" : null, normSize, (useBits ? sizeSuffixesBit : sizeSuffixes)[iUnit]);
	}

	[GeneratedRegex("<.*?>")]
	private static partial Regex RegexStripHtml();
}
