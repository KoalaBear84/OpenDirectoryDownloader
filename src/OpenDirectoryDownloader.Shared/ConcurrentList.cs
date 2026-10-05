using System.Collections;

namespace OpenDirectoryDownloader.Shared;

/// <summary>
/// https://stackoverflow.com/a/28508331/951001
/// Yes, I am using it despite the negative comments. It's only to avoid exception when calculating statistics while still indexing
/// </summary>
/// <remarks>
/// Every WebDirectory owns two of these, and a big scan creates millions of WebDirectory objects, so the
/// per-instance cost matters: it locks with a plain monitor on the list itself (a ReaderWriterLockSlim per
/// list cost over 100 bytes each) and starts with the shared empty array instead of allocating one.
/// The trade-off is that concurrent readers now take turns instead of reading in parallel. Reads are short
/// and each list is touched by few threads, so that is cheap compared with the memory saved.
/// </remarks>
/// <typeparam name="T"></typeparam>
public class ConcurrentList<T> : IList<T>, IDisposable
{
	private const int MinimumGrowCapacity = 4;

	private int _count = 0;
	private T[] _arr;

	public int Count
	{
		get
		{
			lock (this)
			{
				return _count;
			}
		}
	}

	public int InternalArrayLength
	{
		get
		{
			lock (this)
			{
				return _arr.Length;
			}
		}
	}

	public ConcurrentList(int initialCapacity) => _arr = initialCapacity == 0 ? [] : new T[initialCapacity];

	public ConcurrentList() => _arr = [];

	public ConcurrentList(IEnumerable<T> items)
	{
		_arr = items.ToArray();
		_count = _arr.Length;
	}

	public void Add(T item)
	{
		lock (this)
		{
			int newCount = _count + 1;
			EnsureCapacity(newCount);
			_arr[_count] = item;
			_count = newCount;
		}
	}

	public void AddRange(IEnumerable<T> items)
	{
		if (items == null)
		{
			throw new ArgumentNullException("items");
		}

		lock (this)
		{
			T[] arr = items as T[] ?? items.ToArray();
			int newCount = _count + arr.Length;
			EnsureCapacity(newCount);
			Array.Copy(arr, 0, _arr, _count, arr.Length);
			_count = newCount;
		}
	}

	private void EnsureCapacity(int capacity)
	{
		if (_arr.Length >= capacity)
		{
			return;
		}

		int doubled;

		checked
		{
			try
			{
				doubled = _arr.Length * 2;
			}
			catch (OverflowException)
			{
				doubled = int.MaxValue;
			}
		}

		int newLength = Math.Max(Math.Max(doubled, capacity), MinimumGrowCapacity);
		Array.Resize(ref _arr, newLength);
	}

	public bool Remove(T item)
	{
		lock (this)
		{
			int i = IndexOfInternal(item);

			if (i == -1)
			{
				return false;
			}

			RemoveAtInternal(i);

			return true;
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		// Held for the whole enumeration, like the read lock it replaces: writers wait until it finishes
		lock (this)
		{
			for (int i = 0; i < _count; i++)
			{
				yield return _arr[i];
			}
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public int IndexOf(T item)
	{
		lock (this)
		{
			return IndexOfInternal(item);
		}
	}

	private int IndexOfInternal(T item)
	{
		return Array.FindIndex(_arr, 0, _count, x => x.Equals(item));
	}

	public void Insert(int index, T item)
	{
		lock (this)
		{
			if (index > _count)
			{
				throw new ArgumentOutOfRangeException("index");
			}

			int newCount = _count + 1;
			EnsureCapacity(newCount);

			// shift everything right by one, starting at index
			Array.Copy(_arr, index, _arr, index + 1, _count - index);

			// insert
			_arr[index] = item;
			_count = newCount;
		}
	}

	public void RemoveAt(int index)
	{
		lock (this)
		{
			if (index >= _count)
			{
				throw new ArgumentOutOfRangeException("index");
			}

			RemoveAtInternal(index);
		}
	}

	private void RemoveAtInternal(int index)
	{
		Array.Copy(_arr, index + 1, _arr, index, _count - index - 1);
		_count--;

		// release last element
		Array.Clear(_arr, _count, 1);
	}

	public void Clear()
	{
		lock (this)
		{
			Array.Clear(_arr, 0, _count);
			_count = 0;
		}
	}

	public bool Contains(T item)
	{
		lock (this)
		{
			return IndexOfInternal(item) != -1;
		}
	}

	public void CopyTo(T[] array, int arrayIndex)
	{
		lock (this)
		{
			if (_count > array.Length - arrayIndex)
			{
				throw new ArgumentException("Destination array was not long enough.");
			}

			Array.Copy(_arr, 0, array, arrayIndex, _count);
		}
	}

	public bool IsReadOnly => false;

	public T this[int index]
	{
		get
		{
			lock (this)
			{
				if (index >= _count)
				{
					throw new ArgumentOutOfRangeException("index");
				}

				return _arr[index];
			}
		}
		set
		{
			lock (this)
			{
				if (index >= _count)
				{
					throw new ArgumentOutOfRangeException("index");
				}

				_arr[index] = value;
			}
		}
	}

	public void DoSync(Action<ConcurrentList<T>> action)
	{
		GetSync(l =>
		{
			action(l);
			return 0;
		});
	}

	public TResult GetSync<TResult>(Func<ConcurrentList<T>, TResult> func)
	{
		lock (this)
		{
			return func(this);
		}
	}

	/// <summary>Nothing to release any more (it used to dispose its ReaderWriterLockSlim); kept so existing callers still compile.</summary>
	public void Dispose()
	{
	}
}
