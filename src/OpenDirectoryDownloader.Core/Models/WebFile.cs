using System.Diagnostics;

namespace OpenDirectoryDownloader.Models;

[DebuggerDisplay("{Url}, {FileSize} bytes")]
public sealed class WebFile
{
	/// <summary>The URL, or only the part after the parent directory's URL once <see cref="AttachTo"/> found it below it.</summary>
	private string _url;
	private bool _relativeToParent;

	/// <summary>
	/// The absolute URL. Most files live directly below their directory, so once attached a file only keeps the part
	/// after the directory's URL: for a large listing that is most of the memory a file takes.
	/// </summary>
	public string Url
	{
		get => _relativeToParent ? string.Concat(ParentDirectory.Url, _url) : _url;
		set
		{
			_url = value;
			_relativeToParent = false;
		}
	}

	public string FileName { get; set; }

	public long? FileSize { get; set; }

	public string Description { get; set; }

	/// <summary>Row id in the scan database, 0 while it has not been stored yet.</summary>
	public long Id { get; set; }

	/// <summary>Directory this file belongs to, set once it is part of the scanned tree.</summary>
	public WebDirectory ParentDirectory { get; set; }

	/// <summary>Links the file to its directory and stores its URL relative to it, when it is below it.</summary>
	public void AttachTo(WebDirectory parent)
	{
		string url = Url;
		string parentUrl = parent.Url;

		ParentDirectory = parent;

		if (url is not null && parentUrl is not null && url.Length > parentUrl.Length && url.StartsWith(parentUrl, StringComparison.Ordinal))
		{
			_url = url[parentUrl.Length..];
			_relativeToParent = true;
		}
		else
		{
			_url = url;
			_relativeToParent = false;
		}
	}

	/// <summary>A copy of Url/FileName/FileSize/Description, see WebDirectory.Clone().</summary>
	public WebFile Clone()
	{
		return new WebFile
		{
			Url = Url,
			FileName = FileName,
			FileSize = FileSize,
			Description = Description
		};
	}
}
