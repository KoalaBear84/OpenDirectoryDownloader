using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.SystemConsole.Themes;

namespace OpenDirectoryDownloader.Hosting;

/// <summary>
/// The log files: OpenDirectoryDownloader-&lt;date&gt;.log for everything (buffered, written in the background), the
/// history log for the statistics of every finished scan, and warnings and errors on the console.
/// </summary>
public static class Logging
{
	/// <summary>The category that goes to the history log</summary>
	public const string HistoryCategory = "History";

	private const string ConsoleTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

	/// <summary>Set ODD_LOG_LEVEL=Debug (or Verbose) for the per-directory and per-file details</summary>
	public static Serilog.Core.Logger CreateLogger()
	{
		LogEventLevel minimumLevel = Enum.TryParse(Environment.GetEnvironmentVariable("ODD_LOG_LEVEL"), ignoreCase: true, out LogEventLevel level) ? level : LogEventLevel.Information;

		return new LoggerConfiguration()
			.MinimumLevel.Is(minimumLevel)
			.WriteTo.Logger(main => main
				.Filter.ByExcluding(Matching.FromSource(HistoryCategory))
				.WriteTo.Async(file => file.File("OpenDirectoryDownloader-.log", rollingInterval: RollingInterval.Day, buffered: true, flushToDiskInterval: TimeSpan.FromSeconds(1)), bufferSize: 100_000, blockWhenFull: false))
			.WriteTo.Logger(history => history
				.Filter.ByIncludingOnly(Matching.FromSource(HistoryCategory))
				.WriteTo.File("OpenDirectoryDownloader-History.log"))
			.WriteTo.Console(outputTemplate: ConsoleTemplate, restrictedToMinimumLevel: LogEventLevel.Warning, theme: AnsiConsoleTheme.Code)
			.CreateLogger();
	}
}

/// <summary>The history log: one entry per finished scan, kept apart from the regular log.</summary>
public interface IHistoryLog
{
	void Write(string message);
}

public sealed class HistoryLog(ILoggerFactory loggerFactory) : IHistoryLog
{
	private readonly Microsoft.Extensions.Logging.ILogger _logger = loggerFactory.CreateLogger(Logging.HistoryCategory);

	public void Write(string message) => _logger.LogInformation("{message}", message);
}

/// <summary>The console window's title.</summary>
public sealed class ConsoleTitle
{
	public string Current { get; private set; } = "OpenDirectoryDownloader";

	public void Set(string title)
	{
		Current = title;

		try
		{
			Console.Title = title;
		}
		catch (Exception)
		{
			// Not every console has a title
		}
	}
}
