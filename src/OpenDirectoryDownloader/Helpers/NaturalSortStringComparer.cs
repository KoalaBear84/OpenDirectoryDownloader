namespace OpenDirectoryDownloader.Helpers;

public class NaturalSortStringComparer : IComparer<string>
{
	public static NaturalSortStringComparer Ordinal { get; } = new NaturalSortStringComparer(StringComparison.Ordinal);
	public static NaturalSortStringComparer OrdinalIgnoreCase { get; } = new NaturalSortStringComparer(StringComparison.OrdinalIgnoreCase);
	public static NaturalSortStringComparer CurrentCulture { get; } = new NaturalSortStringComparer(StringComparison.CurrentCulture);
	public static NaturalSortStringComparer CurrentCultureIgnoreCase { get; } = new NaturalSortStringComparer(StringComparison.CurrentCultureIgnoreCase);
	public static NaturalSortStringComparer InvariantCulture { get; } = new NaturalSortStringComparer(StringComparison.InvariantCulture);
	public static NaturalSortStringComparer InvariantCultureIgnoreCase { get; } = new NaturalSortStringComparer(StringComparison.InvariantCultureIgnoreCase);

	private readonly StringComparison _comparison;

	public NaturalSortStringComparer(StringComparison comparison)
	{
		_comparison = comparison;
	}

	public int Compare(string x, string y)
	{
		// Let string.Compare handle the case where x or y is null
		if (x is null || y is null)
		{
			return string.Compare(x, y, _comparison);
		}

		int cmp;

		// Segments before the first difference are identical in both strings and compare equal, so start there
		int start = CommonSegmentStart(x, y);
		StringSegmentEnumerator xSegments = new(x, start);
		StringSegmentEnumerator ySegments = new(y, start);

		while (xSegments.MoveNext() && ySegments.MoveNext())
		{
			// If they're both numbers, compare the value
			if (xSegments.CurrentIsNumber && ySegments.CurrentIsNumber)
			{
				cmp = ParseNumber(xSegments.Current).CompareTo(ParseNumber(ySegments.Current));

				if (cmp != 0)
				{
					return cmp;
				}
			}
			// If x is a number and y is not, x is "lesser than" y
			else if (xSegments.CurrentIsNumber)
			{
				return -1;
			}
			// If y is a number and x is not, x is "greater than" y
			else if (ySegments.CurrentIsNumber)
			{
				return 1;
			}

			// OK, neither are number, compare the segments as text
			cmp = xSegments.Current.CompareTo(ySegments.Current, _comparison);

			if (cmp != 0)
			{
				return cmp;
			}
		}

		// At this point, either all segments are equal, or one string is shorter than the other

		// If x is shorter, it's "lesser than" y
		if (x.Length < y.Length)
		{
			return -1;
		}

		// If x is longer, it's "greater than" y
		if (x.Length > y.Length)
		{
			return 1;
		}

		// If they have the same length, they're equal
		return 0;
	}

	/// <summary>
	/// Where the first segment that can differ starts. URLs in one listing share a long prefix (scheme, host,
	/// path), and walking all of its segments on every comparison dominated sorting big lists. The strings are
	/// identical up to their first different character; the segment containing it starts at the nearest
	/// earlier position that is a segment boundary in both strings. Everything before that compares equal
	/// segment by segment, so skipping it gives exactly the same result.
	/// </summary>
	private static int CommonSegmentStart(string x, string y)
	{
		int max = Math.Min(x.Length, y.Length);
		int position = 0;

		while (position < max && x[position] == y[position])
		{
			position++;
		}

		if (position == 0 || position >= max)
		{
			return 0;
		}

		while (position > 0 && (char.IsDigit(x[position - 1]) == char.IsDigit(x[position]) || char.IsDigit(y[position - 1]) == char.IsDigit(y[position])))
		{
			position--;
		}

		return position;
	}

	/// <summary>The value long.TryParse gives (0 when it fails), without its culture handling for the plain ASCII digit runs that nearly all numbers are.</summary>
	private static long ParseNumber(ReadOnlySpan<char> digits)
	{
		if (digits.Length <= 18)
		{
			long value = 0;

			foreach (char c in digits)
			{
				if (c is < '0' or > '9')
				{
					return ParseNumberSlow(digits);
				}

				value = value * 10 + (c - '0');
			}

			return value;
		}

		return ParseNumberSlow(digits);
	}

	private static long ParseNumberSlow(ReadOnlySpan<char> digits)
	{
		_ = long.TryParse(digits, out long value);

		return value;
	}

	private struct StringSegmentEnumerator
	{
		private readonly string _s;
		private int _start;
		private int _length;

		public StringSegmentEnumerator(string s, int start)
		{
			_s = s;
			_start = start;
			_length = 0;
			CurrentIsNumber = false;
		}

		public ReadOnlySpan<char> Current => _s.AsSpan(_start, _length);

		public bool CurrentIsNumber { get; private set; }

		public bool MoveNext()
		{
			int currentPosition = _start >= 0
				? _start + _length
				: 0;

			if (currentPosition >= _s.Length)
			{
				return false;
			}

			int start = currentPosition;
			bool isFirstCharDigit = char.IsDigit(_s[currentPosition]);

			while (++currentPosition < _s.Length && char.IsDigit(_s[currentPosition]) == isFirstCharDigit)
			{
			}

			_start = start;
			_length = currentPosition - start;
			CurrentIsNumber = isFirstCharDigit;

			return true;
		}
	}
}