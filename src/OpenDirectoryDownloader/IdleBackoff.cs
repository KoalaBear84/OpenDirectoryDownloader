namespace OpenDirectoryDownloader;

/// <summary>
/// How long a crawl worker waits before looking at an empty queue again. It starts short, so work that
/// appears just after a worker looked (the next level of a directory tree, the end of the scan) is picked up
/// within a few milliseconds, and doubles up to <see cref="Maximum"/> while the queue stays empty, so a long
/// slow request does not leave twenty workers waking up hundreds of times a second. Reset it when work is found.
///
/// The workers used to wait a fixed second, and then a fixed 100 ms. Measured with 20 threads (best of 5), this
/// takes a 21-directory scan from 209 to about 33 ms, a 585-directory scan from 591 to about 430 ms and a
/// 4,681-directory scan from 2,326 to about 2,200 ms. That is what replacing the queues with Channels would have
/// gained, without changing how the workers decide to stop. The cap hardly matters for speed (10 to 100 ms all
/// measured within noise) and 25 ms still means only about 40 wake-ups a second for a worker that has nothing to do.
/// </summary>
public struct IdleBackoff
{
	public static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(5);

	public static readonly TimeSpan Maximum = TimeSpan.FromMilliseconds(25);

	private TimeSpan _next;

	/// <summary>The wait to use now; the one after it is twice as long, up to <see cref="Maximum"/>.</summary>
	public TimeSpan NextWait()
	{
		TimeSpan wait = _next == TimeSpan.Zero ? Minimum : _next;

		_next = wait * 2 > Maximum ? Maximum : wait * 2;

		return wait;
	}

	/// <summary>Work was found: the next idle period starts short again.</summary>
	public void Reset() => _next = TimeSpan.Zero;
}
