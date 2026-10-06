namespace OpenDirectoryDownloader.Scanning;

/// <summary>Messages for (and the odd question to) the person running the scan, kept apart from the log.</summary>
public interface IUserConsole
{
	/// <summary>Whether a person can answer a question; false when input is redirected or the scan runs unattended.</summary>
	bool IsInteractive { get; }

	void WriteLine(string message);

	/// <summary>Asks a question and returns the answer, or null when nobody can answer.</summary>
	string Ask(string question);
}

/// <summary>Writes to and reads from the process console.</summary>
public sealed class SystemUserConsole(bool interactive) : IUserConsole
{
	public bool IsInteractive { get; } = interactive && !Console.IsInputRedirected;

	public void WriteLine(string message) => Console.WriteLine(message);

	public string Ask(string question)
	{
		Console.WriteLine(question);

		return IsInteractive ? Console.ReadLine() : null;
	}
}

/// <summary>Says nothing and never gets an answer (tests, the web viewer).</summary>
public sealed class NullUserConsole : IUserConsole
{
	public static readonly NullUserConsole Instance = new();

	public bool IsInteractive => false;

	public void WriteLine(string message)
	{
	}

	public string Ask(string question) => null;
}
