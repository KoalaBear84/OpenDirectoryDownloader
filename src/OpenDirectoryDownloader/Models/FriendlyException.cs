using System;

namespace OpenDirectoryDownloader.Models;

public class FriendlyException : Exception
{
	public FriendlyException() : base() { }
	public FriendlyException(string message) : base(message) { }
	public FriendlyException(string message, Exception innerException) : base(message, innerException) { }
}
