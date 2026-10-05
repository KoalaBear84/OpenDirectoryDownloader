namespace OpenDirectoryDownloader.Tests;

// Verbatim copy of ReferenceNaturalSortStringComparer before its optimization: the reference the new one must agree with


public class ReferenceNaturalSortStringComparer : IComparer<string>
{
	public static ReferenceNaturalSortStringComparer Ordinal { get; } = new ReferenceNaturalSortStringComparer(StringComparison.Ordinal);
	public static ReferenceNaturalSortStringComparer OrdinalIgnoreCase { get; } = new ReferenceNaturalSortStringComparer(StringComparison.OrdinalIgnoreCase);
	public static ReferenceNaturalSortStringComparer CurrentCulture { get; } = new ReferenceNaturalSortStringComparer(StringComparison.CurrentCulture);
	public static ReferenceNaturalSortStringComparer CurrentCultureIgnoreCase { get; } = new ReferenceNaturalSortStringComparer(StringComparison.CurrentCultureIgnoreCase);
	public static ReferenceNaturalSortStringComparer InvariantCulture { get; } = new ReferenceNaturalSortStringComparer(StringComparison.InvariantCulture);
	public static ReferenceNaturalSortStringComparer InvariantCultureIgnoreCase { get; } = new ReferenceNaturalSortStringComparer(StringComparison.InvariantCultureIgnoreCase);

	private readonly StringComparison _comparison;

	public ReferenceNaturalSortStringComparer(StringComparison comparison)
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

		StringSegmentEnumerator xSegments = GetSegments(x);
		StringSegmentEnumerator ySegments = GetSegments(y);

		while (xSegments.MoveNext() && ySegments.MoveNext())
		{
			// If they're both numbers, compare the value
			if (xSegments.CurrentIsNumber && ySegments.CurrentIsNumber)
			{
				_ = long.TryParse(xSegments.Current, out long xValue);
				_ = long.TryParse(ySegments.Current, out long yValue);
				cmp = xValue.CompareTo(yValue);

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

	private static StringSegmentEnumerator GetSegments(string s) => new(s);

	private struct StringSegmentEnumerator
	{
		private readonly string _s;
		private int _start;
		private int _length;

		public StringSegmentEnumerator(string s)
		{
			_s = s;
			_start = -1;
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