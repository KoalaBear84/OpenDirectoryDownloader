namespace OpenDirectoryDownloader.Models;

/// <summary>Retry without logging the attempt (the reason was already logged, like a Retry-After wait).</summary>
public class SilentException : Exception;
