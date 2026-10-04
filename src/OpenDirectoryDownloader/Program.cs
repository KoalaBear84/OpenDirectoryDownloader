using CommandLine;
using OpenDirectoryDownloader.Shared.Models;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;
using System.Text;

namespace OpenDirectoryDownloader;

public class Program
{
	public static Serilog.Core.Logger Logger;
	public static Serilog.Core.Logger HistoryLogger;

	public static string ConsoleTitle { get; set; }
	private static CommandLineOptions CommandLineOptions { get; set; }
	private static Process FlareSolverrLogProcess { get; set; }

	public static async Task<int> Main(string[] args)
	{
		SetConsoleTitle("OpenDirectoryDownloader");

		Console.OutputEncoding = Encoding.UTF8;

		Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.File("OpenDirectoryDownloader-.log", rollingInterval: RollingInterval.Day)
			.WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: LogEventLevel.Warning, theme: AnsiConsoleTheme.Code)
			.CreateLogger();

		HistoryLogger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.File("OpenDirectoryDownloader-History.log")
			.WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}", restrictedToMinimumLevel: LogEventLevel.Warning, theme: AnsiConsoleTheme.Code)
			.CreateLogger();

		// Dragging a .sqlite scan database straight onto the .exe in Explorer launches this process with
		// that file as its only argument - rewrite it into `--serve --serve-db <path>` before the normal
		// parser ever sees it, so --serve-port/--serve-host/etc. still get their proper [Option] defaults
		// (constructing a CommandLineOptions directly, bypassing the parser, would leave those at 0/null).
		// Positional arguments aren't otherwise supported by CommandLineOptions.
		if (args.Length == 1 && File.Exists(args[0]) && Path.GetExtension(args[0]).Equals(".sqlite", StringComparison.OrdinalIgnoreCase))
		{
			args = ["--serve", "--serve-db", args[0]];
		}

		Process currentProcess = Process.GetCurrentProcess();

		Console.WriteLine($"Started with PID {currentProcess.Id}");
		Logger.Information("Started with PID {processId}", currentProcess.Id);

		Thread.CurrentThread.Name = "Main thread";

		bool stopProcessing = false;

		Parser parser = new(with =>
		{
			with.AllowMultiInstance = true;
			with.HelpWriter = Console.Error;
		});

		// CommandLineParser blindly consumes whatever token follows "--http-cloak" as its value, even if that token
		// is itself a valid option (e.g. "--http-cloak --url X" ends up with HttpCloak = "url" and Url unset). Strip
		// "--http-cloak" (and a following value, if any) out of the raw args ourselves before the rest get parsed,
		// so its position relative to other options can no longer corrupt them.
		(List<string> remainingArgs, string httpCloakPreset) = ExtractHttpCloakOption(args);

		parser.ParseArguments<CommandLineOptions>(remainingArgs)
			.WithNotParsed(o =>
			{
				List<Error> errors = o.ToList();

				stopProcessing = errors.Any(e => e.StopsProcessing || e.Tag == ErrorType.UnknownOptionError);

				if (errors.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.VersionRequestedError || e.Tag == ErrorType.UnknownOptionError))
				{
					return;
				}

				foreach (Error error in errors)
				{
					Console.WriteLine($"Error command line parameter '{error.Tag}'");
				}
			})
			.WithParsed(o => CommandLineOptions = o);

		if (stopProcessing)
		{
			return 1;
		}

		if (httpCloakPreset is not null)
		{
			CommandLineOptions.HttpCloak = httpCloakPreset.Length > 0 ? httpCloakPreset : "chrome-latest";
		}

		if (CommandLineOptions.Serve)
		{
			return await Server.ScanDatabaseServer.RunAsync(CommandLineOptions, Logger);
		}

		if (CommandLineOptions.Threads is < 1 or > 100)
		{
			Console.WriteLine("Threads must be between 1 and 100");
			return 1;
		}

		if (CommandLineOptions.Threads > 1 && CommandLineOptions.WaitSecondsBetweenCalls > 0)
		{
			Logger.Information("Using a wait time with more than 1 thread isn't recommended as it will still have multiple threads running.");
			return 1;
		}

		string url = CommandLineOptions.Url;

		if (string.IsNullOrWhiteSpace(url))
		{
			Console.WriteLine("Which URL do you want to index?");
			url = Console.ReadLine();

			if (url?.Equals("quit", StringComparison.OrdinalIgnoreCase) == true || url?.Equals("exit", StringComparison.OrdinalIgnoreCase) == true)
			{
				return 0;
			}
		}

		OpenDirectoryIndexerSettings openDirectoryIndexerSettings = new()
		{
			CommandLineOptions = CommandLineOptions
		};

		if (File.Exists(url))
		{
			openDirectoryIndexerSettings.FileName = url;
		}
		else
		{
			Console.WriteLine($"URL specified: {url}");

			string newUrl = Library.FixUrl(url);

			if (newUrl != url)
			{
				Console.WriteLine($"URL fixed    : {newUrl}");
			}

			openDirectoryIndexerSettings.Url = newUrl;
		}

		openDirectoryIndexerSettings.Threads = openDirectoryIndexerSettings.CommandLineOptions.Threads;
		openDirectoryIndexerSettings.Timeout = openDirectoryIndexerSettings.CommandLineOptions.Timeout;
		openDirectoryIndexerSettings.Username = openDirectoryIndexerSettings.CommandLineOptions.Username;
		openDirectoryIndexerSettings.Password = openDirectoryIndexerSettings.CommandLineOptions.Password;

		if (string.IsNullOrEmpty(openDirectoryIndexerSettings.Username) && string.IsNullOrEmpty(openDirectoryIndexerSettings.Password))
		{
			if (Library.GetUriCredentials(new Uri(openDirectoryIndexerSettings.Url), out string username, out string password))
			{
				Console.WriteLine($"Using username '{username}' and password '{password}'");
				openDirectoryIndexerSettings.Username = username;
				openDirectoryIndexerSettings.Password = password;
			}
		}

		// FTP
		if (openDirectoryIndexerSettings.Url?.StartsWith(Constants.UriScheme.Ftp) == true || openDirectoryIndexerSettings.Url?.StartsWith(Constants.UriScheme.Ftps) == true)
		{
			openDirectoryIndexerSettings.Threads = 6;
		}

		// Translates . and .. etc
		if (openDirectoryIndexerSettings.CommandLineOptions.OutputFile is not null)
		{
			openDirectoryIndexerSettings.CommandLineOptions.OutputFile = Path.GetFullPath(openDirectoryIndexerSettings.CommandLineOptions.OutputFile);
		}

		OpenDirectoryIndexer openDirectoryIndexer = new(openDirectoryIndexerSettings);

		SetConsoleTitle($"{new Uri(openDirectoryIndexerSettings.Url).Host.Replace("www.", string.Empty)} - {ConsoleTitle}");
		StartFlareSolverrLogStreaming();

		try
		{
			openDirectoryIndexer.StartIndexingAsync();
			Console.WriteLine("Started indexing!");

			Command.ShowInfoAndCommands();
			Command.ProcessConsoleInput(openDirectoryIndexer);

			await openDirectoryIndexer.IndexingTask;

			if (CommandLineOptions.Quit)
			{
				return 0;
			}

			Console.WriteLine("Press ESC to exit");
			Console.ReadKey();

			return 0;
		}
		finally
		{
			StopFlareSolverrLogStreaming();
		}
	}

	private static void StartFlareSolverrLogStreaming()
	{
		if (string.IsNullOrWhiteSpace(CommandLineOptions.FlareSolverrDockerName))
		{
			return;
		}

		try
		{
			ProcessStartInfo processStartInfo = new("docker")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			processStartInfo.ArgumentList.Add("logs");
			processStartInfo.ArgumentList.Add("--tail");
			processStartInfo.ArgumentList.Add("0");
			processStartInfo.ArgumentList.Add("--follow");
			processStartInfo.ArgumentList.Add(CommandLineOptions.FlareSolverrDockerName);

			FlareSolverrLogProcess = new Process
			{
				StartInfo = processStartInfo
			};

			FlareSolverrLogProcess.OutputDataReceived += (_, e) => WriteFlareSolverrLogLine(e.Data, isError: false);
			FlareSolverrLogProcess.ErrorDataReceived += (_, e) => WriteFlareSolverrLogLine(e.Data, isError: true);

			if (!FlareSolverrLogProcess.Start())
			{
				Logger.Warning("Failed to start FlareSolverr Docker log streaming for container '{containerName}'", CommandLineOptions.FlareSolverrDockerName);
				FlareSolverrLogProcess.Dispose();
				FlareSolverrLogProcess = null;
				return;
			}

			Logger.Warning("Streaming FlareSolverr Docker logs from container '{containerName}'", CommandLineOptions.FlareSolverrDockerName);
			FlareSolverrLogProcess.BeginOutputReadLine();
			FlareSolverrLogProcess.BeginErrorReadLine();
		}
		catch (Exception ex)
		{
			Logger.Warning(ex, "Failed to start FlareSolverr Docker log streaming for container '{containerName}'", CommandLineOptions.FlareSolverrDockerName);
		}
	}

	private static void WriteFlareSolverrLogLine(string logLine, bool isError)
	{
		if (string.IsNullOrWhiteSpace(logLine))
		{
			return;
		}

		string formattedLogLine = $"[FlareSolverr] {logLine}";

		if (isError)
		{
			Logger.Warning("{logLine}", formattedLogLine);
			return;
		}

		Console.WriteLine(formattedLogLine);
		Logger.Information("{logLine}", formattedLogLine);
	}

	private static void StopFlareSolverrLogStreaming()
	{
		if (FlareSolverrLogProcess is null)
		{
			return;
		}

		try
		{
			if (!FlareSolverrLogProcess.HasExited)
			{
				FlareSolverrLogProcess.Kill(true);
				FlareSolverrLogProcess.WaitForExit(2000);
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(ex, "Failed to stop FlareSolverr Docker log streaming for container '{containerName}'", CommandLineOptions.FlareSolverrDockerName);
		}
		finally
		{
			FlareSolverrLogProcess.Dispose();
			FlareSolverrLogProcess = null;
		}
	}

	public static void SetConsoleTitle(string title)
	{
		ConsoleTitle = title;

		Console.Title = title;
	}

	/// <summary>
	/// Pulls "--http-cloak" (bare, "--http-cloak value" or "--http-cloak=value") out of the raw args.
	/// </summary>
	/// <returns>The remaining args to hand to CommandLineParser, and the requested preset: null if "--http-cloak"
	/// wasn't specified at all, or an empty string if it was specified without a preset.</returns>
	private static (List<string> RemainingArgs, string HttpCloakPreset) ExtractHttpCloakOption(string[] args)
	{
		const string option = "--http-cloak";

		List<string> remainingArgs = [.. args];
		string preset = null;

		for (int i = 0; i < remainingArgs.Count; i++)
		{
			string arg = remainingArgs[i];

			if (arg.StartsWith($"{option}=", StringComparison.Ordinal))
			{
				preset = arg[(option.Length + 1)..];
				remainingArgs.RemoveAt(i);
				break;
			}

			if (arg == option)
			{
				remainingArgs.RemoveAt(i);

				if (i < remainingArgs.Count && !remainingArgs[i].StartsWith('-'))
				{
					preset = remainingArgs[i];
					remainingArgs.RemoveAt(i);
				}
				else
				{
					preset = string.Empty;
				}

				break;
			}
		}

		return (remainingArgs, preset);
	}
}
