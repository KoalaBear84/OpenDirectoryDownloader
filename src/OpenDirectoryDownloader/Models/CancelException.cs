namespace OpenDirectoryDownloader.Models;

[Serializable]
internal class CancelException : Exception
{
	public CancelException(string message) : base(message)
	{
	}
}
