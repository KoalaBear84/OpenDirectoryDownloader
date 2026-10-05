namespace OpenDirectoryDownloader;

/// <summary>
/// RegexParser1 (Apache style) and RegexParser2 (nginx style) run a regex on every line of a listing and then only
/// read one or two of its groups. A Match with its groups is 550 to 650 bytes, per line. For the very regular shape
/// those two regexes were written for, the same values can be read directly; anything that deviates in any way is
/// declined (returns false) and the caller runs the regex exactly as before. Each method claims a line only when
/// the regex is certain to match it with these group values (see the comments for how the greedy and lazy parts
/// resolve on that shape); LineRegexFastPathTests compares every claim with the regex on real and mutated lines.
/// </summary>
public static class LineRegexFastPath
{
	/// <summary>
	/// nginx autoindex rows: <c>&lt;a href="x"&gt;x&lt;/a&gt;   02-Jan-2024 10:11   1503238553</c>.
	/// Equivalent to RegexRegexParser2 matching with FileSize.Value.Trim() == fileSize.
	/// </summary>
	public static bool TryMatchNginx(string line, out string fileSize)
	{
		fileSize = null;

		// "<a" at the start, exactly one "</a>" and no other '<': the greedy "<a.*</a>" ends at that one closing tag
		if (line.Length < 20 || line[0] != '<' || line[1] != 'a')
		{
			return false;
		}

		int close = line.IndexOf('<', 1);

		if (close < 0 || string.CompareOrdinal(line, close, "</a>", 0, 4) != 0 || line.IndexOf('<', close + 1) >= 0 || !OnlyPlainSpaces(line))
		{
			return false;
		}

		int position = close + 4;
		SkipSpaces(line, ref position);

		// DateTime: \d+-\w+-\d+\s\d+:\d{0,2}  (one space between date and time, 1 or 2 minute digits)
		if (!SkipDigits(line, ref position) || !Skip(line, ref position, '-') || !SkipWordChars(line, ref position) || !Skip(line, ref position, '-') ||
		    !SkipDigits(line, ref position) || !Skip(line, ref position, ' ') || !SkipDigits(line, ref position) || !Skip(line, ref position, ':'))
		{
			return false;
		}

		int minutesStart = position;

		SkipDigitsOptional(line, ref position);

		int minutes = position - minutesStart;

		// \d{0,2} takes at most two digits; anything left over would belong to the next token
		if (minutes is < 1 or > 2 || position >= line.Length || line[position] != ' ')
		{
			return false;
		}

		// \s*(?<FileSize>\S+\s?\S*)?\s*\S*: one token, then only spaces. (Trimmed, the group is that token.)
		SkipSpaces(line, ref position);

		int tokenStart = position;

		while (position < line.Length && line[position] != ' ')
		{
			position++;
		}

		if (position == tokenStart)
		{
			return false;
		}

		int tokenEnd = position;

		SkipSpaces(line, ref position);

		if (position != line.Length)
		{
			return false;
		}

		fileSize = line.Substring(tokenStart, tokenEnd - tokenStart);

		return true;
	}

	/// <summary>
	/// Apache mod_autoindex rows: <c>&lt;img src="/icons/unknown.gif" alt="[   ]"&gt; &lt;a href="x"&gt;x&lt;/a&gt;   2024-01-02 10:11  1.4G   </c>.
	/// Equivalent to RegexRegexParser1 matching with these FileSize.Value and Description.Value.
	/// </summary>
	public static bool TryMatchApache(string line, out string fileSize, out string description)
	{
		fileSize = null;
		description = null;

		if (line.Length < 30 || string.CompareOrdinal(line, 0, "<img", 0, 4) != 0 || !OnlyPlainSpaces(line))
		{
			return false;
		}

		// Exactly the three tags <img ...>, <a ...>, </a>: three '<' and three '>' in that order. That fixes what the
		// greedy "<img.*>" (it backs off to the '>' that is followed by "<a") and the lazy "<a.*?>.*?</a>" match.
		int imgEnd = line.IndexOf('>');
		int anchorStart = imgEnd < 0 ? -1 : line.IndexOf('<', imgEnd + 1);

		if (imgEnd < 0 || anchorStart < 0 || anchorStart + 1 >= line.Length || line[anchorStart + 1] != 'a')
		{
			return false;
		}

		for (int i = imgEnd + 1; i < anchorStart; i++)
		{
			if (line[i] != ' ')
			{
				return false;
			}
		}

		int anchorEnd = line.IndexOf('>', anchorStart);
		int close = anchorEnd < 0 ? -1 : line.IndexOf('<', anchorEnd + 1);

		if (anchorEnd < 0 || close < 0 || string.CompareOrdinal(line, close, "</a>", 0, 4) != 0 || line.IndexOf('<', close + 1) >= 0 || line.IndexOf('>', close + 4) >= 0 || line.IndexOf('>', close) != close + 3 || line.IndexOf('>', anchorEnd + 1) != close + 3)
		{
			return false;
		}

		// "<img" starts at 0 and has no '<' inside it
		if (line.IndexOf('<', 1) != anchorStart)
		{
			return false;
		}

		int position = close + 4;

		// \S* (nothing may be glued to the closing tag) then \s*
		if (position >= line.Length || line[position] != ' ')
		{
			return false;
		}

		SkipSpaces(line, ref position);

		// Modified: \d*-(?:[a-zA-Z]*|\d*)-\d*\s*\d*:\d*  as 2024-01-02 10:11 or 02-Jan-2024 10:11 (no seconds)
		if (!SkipDigits(line, ref position) || !Skip(line, ref position, '-'))
		{
			return false;
		}

		int monthStart = position;

		if (position < line.Length && IsAsciiLetter(line[position]))
		{
			while (position < line.Length && IsAsciiLetter(line[position]))
			{
				position++;
			}
		}
		else
		{
			SkipDigitsOptional(line, ref position);
		}

		if (position == monthStart || !Skip(line, ref position, '-') || !SkipDigits(line, ref position))
		{
			return false;
		}

		int beforeTimeSpaces = position;

		SkipSpaces(line, ref position);

		if (position == beforeTimeSpaces || !SkipDigits(line, ref position) || !Skip(line, ref position, ':') || !SkipDigits(line, ref position))
		{
			return false;
		}

		// the date must end here: seconds (':'), or anything glued to the minutes, is left to the regex
		if (position < line.Length && line[position] != ' ')
		{
			return false;
		}

		// \s*(?<FileSize>\S+)? then (\s*(?<Description>.*))?
		SkipSpaces(line, ref position);

		int tokenStart = position;

		while (position < line.Length && line[position] != ' ')
		{
			position++;
		}

		if (position == tokenStart)
		{
			return false;
		}

		int tokenEnd = position;

		SkipSpaces(line, ref position);

		fileSize = line.Substring(tokenStart, tokenEnd - tokenStart);
		description = line.Substring(position);

		return true;
	}

	/// <summary>No line breaks and no whitespace other than a plain space anywhere: \s and \S then split the line exactly where this code does.</summary>
	private static bool OnlyPlainSpaces(string line)
	{
		foreach (char c in line)
		{
			if (c != ' ' && char.IsWhiteSpace(c))
			{
				return false;
			}
		}

		return true;
	}

	private static void SkipSpaces(string line, ref int position)
	{
		while (position < line.Length && line[position] == ' ')
		{
			position++;
		}
	}

	private static bool Skip(string line, ref int position, char expected)
	{
		if (position < line.Length && line[position] == expected)
		{
			position++;

			return true;
		}

		return false;
	}

	private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

	private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

	/// <summary>One or more ASCII digits.</summary>
	private static bool SkipDigits(string line, ref int position)
	{
		int start = position;

		SkipDigitsOptional(line, ref position);

		return position > start;
	}

	private static void SkipDigitsOptional(string line, ref int position)
	{
		while (position < line.Length && IsAsciiDigit(line[position]))
		{
			position++;
		}
	}

	/// <summary>One or more of the ASCII characters \w matches (letters, digits, underscore).</summary>
	private static bool SkipWordChars(string line, ref int position)
	{
		int start = position;

		while (position < line.Length && (IsAsciiLetter(line[position]) || IsAsciiDigit(line[position]) || line[position] == '_'))
		{
			position++;
		}

		return position > start;
	}
}
