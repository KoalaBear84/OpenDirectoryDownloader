namespace OpenDirectoryDownloader.Shared;

/// <summary>
/// A small, allocation-free, non-cryptographic 128-bit hash made of two independent 64-bit lanes (FNV-1a
/// style, each with its own seed and odd multiplier, finished with an avalanche step). Used where only
/// equality matters and keeping the original strings around would cost memory: the directory content
/// fingerprint and the set of processed directory URLs. 128 bits makes an accidental collision between
/// two different inputs practically impossible, even across hundreds of millions of URLs.
/// </summary>
public struct Hash128
{
	private const ulong FirstSeed = 14695981039346656037;
	private const ulong FirstMultiplier = 1099511628211;
	private const ulong SecondSeed = 0x9E3779B97F4A7C15;
	// Must be odd: an even multiplier shifts the earlier input out of the state and the lane would only reflect the last ~64 characters
	private const ulong SecondMultiplier = 0xC2B2AE3D27D4EB4F;

	private ulong _first;
	private ulong _second;

	public Hash128()
	{
		_first = FirstSeed;
		_second = SecondSeed;
	}

	public void Add(char c)
	{
		_first = (_first ^ c) * FirstMultiplier;
		_second = (_second ^ c) * SecondMultiplier;
	}

	public void Add(string value)
	{
		if (value is null)
		{
			Add((char)0);
			return;
		}

		foreach (char c in value)
		{
			Add(c);
		}
	}

	public void Add(long value)
	{
		for (int shift = 0; shift < 64; shift += 16)
		{
			Add((char)(value >> shift));
		}
	}

	public readonly UInt128 Result => new(Mix(_first), Mix(_second ^ (_first >> 17)));

	public static UInt128 Of(string value)
	{
		Hash128 hash = new();
		hash.Add(value);

		return hash.Result;
	}

	/// <summary>MurmurHash3's 64-bit finalizer: spreads every input bit over the whole output.</summary>
	private static ulong Mix(ulong value)
	{
		value ^= value >> 33;
		value *= 0xFF51AFD7ED558CCD;
		value ^= value >> 33;
		value *= 0xC4CEB9FE1A85EC53;
		value ^= value >> 33;

		return value;
	}
}
