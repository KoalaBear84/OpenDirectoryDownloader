namespace OpenDirectoryDownloader.Models;

/// <summary>Stops retrying a directory: the crawler gives up on it right away.</summary>
public class CancelException(string message) : Exception(message);
