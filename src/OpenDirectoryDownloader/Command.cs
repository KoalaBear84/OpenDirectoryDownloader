using OpenDirectoryDownloader.Helpers;
using OpenDirectoryDownloader.Shared.Models;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using TextCopy;

namespace OpenDirectoryDownloader;

public class Command
{
	private static readonly string VersionNumber = Assembly.GetExecutingAssembly().GetName().Version.ToString();

	/// <summary>
	/// Set console properties (Window size)
	/// </summary>
	internal static void SetConsoleProperties()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return;
		}

		Console.WindowWidth = Math.Min(170, Console.LargestWindowWidth);
		Console.WindowHeight = Math.Min(34, Console.LargestWindowHeight);
	}

	internal static void ShowInfoAndCommands()
	{
		Console.WriteLine(
			"┌─────────────────────────────────────────────────────────────────────────┐\n" +
			$"│ KoalaBear84/OpenDirectoryDownloader v{VersionNumber}{string.Empty.PadLeft(36 - VersionNumber.Length - 1)}│\n" +
			"├─────────────────────────────────────────────────────────────────────────┤\n" +
			"│ Press I for info (this)                                                 │\n" +
			"│ Press S for statistics                                                  │\n" +
			"│ Press T for thread info                                                 │\n" +
			"│ Press U for Save TXT                                                    │\n" +
			"│ Press J for Save JSON                                                   │\n" +
			"│ Press P to Pause (stop cleanly, resumable with --resume)                │\n" +
			"├─────────────────────────────────────────────────────────────────────────┤\n" +
			"│ Press ESC or X to EXIT                                                  │\n" +
			"└─────────────────────────────────────────────────────────────────────────┘\n");
	}

	internal static void ProcessConsoleInput(OpenDirectoryIndexer openDirectoryIndexer)
	{
		if (Console.IsInputRedirected)
		{
			string message = "Console input is redirect, maybe it is run inside another host. This could mean that no input will be send/processed.";
			Console.WriteLine(message);
			Program.Logger.Warning(message);
		}

		while (true)
		{
			try
			{
				if (Console.IsInputRedirected)
				{
					int keyPressed = Console.Read();

					if (keyPressed == -1)
					{
						// Needed, when input is redirected it will immediately return with -1
						Task.Delay(10).Wait();
					}

					//if (char.IsControl((char)keyPressed))
					//{
					//    Console.WriteLine($"Pressed Console.Read(): {keyPressed}");
					//}
					//else
					//{
					//    Console.WriteLine($"Pressed Console.Read(): {(char)keyPressed}");
					//}

					switch (keyPressed)
					{
						case 'x':
						case 'X':
							KillApplication();
							break;
						case 'i':
							ShowInfoAndCommands();
							break;
						case 'c':
							if (OpenDirectoryIndexer.Session.Finished != DateTimeOffset.MinValue)
							{
								SetClipboard(Statistics.GetSessionStats(OpenDirectoryIndexer.Session, includeExtensions: true, onlyRedditStats: true, scanDatabase: openDirectoryIndexer.ScanDatabase));
								KillApplication();
							}
							break;
						case 's':
						case 'S':
							ShowStatistics(openDirectoryIndexer);
							break;
						case 't':
						case 'T':
							ShowThreads(openDirectoryIndexer);
							break;
						case 'j':
						case 'J':
							SaveSession(openDirectoryIndexer);
							break;
						case 'p':
						case 'P':
							PauseAndExit(openDirectoryIndexer);
							break;
						default:
							break;
					}
				}
				else
				{
					if (!Console.KeyAvailable)
					{
						Task.Delay(10).Wait();
						continue;
					}

					ConsoleKey keyPressed = Console.ReadKey(intercept: true).Key;
					//Console.WriteLine($"Pressed (Console.ReadKey(): {keyPressed}");

					switch (keyPressed)
					{
						case ConsoleKey.X:
						case ConsoleKey.Escape:
							KillApplication();
							break;
						case ConsoleKey.I:
							ShowInfoAndCommands();
							break;
						case ConsoleKey.C:
							if (OpenDirectoryIndexer.Session.Finished != DateTimeOffset.MinValue)
							{
								try
								{
									SetClipboard(Statistics.GetSessionStats(OpenDirectoryIndexer.Session, includeExtensions: true, onlyRedditStats: true, scanDatabase: openDirectoryIndexer.ScanDatabase));
								}
								catch (Exception ex)
								{
									Program.Logger.Error("Error copying stats to clipboard: {error}", ex.Message);
								}

								KillApplication();
							}
							break;
						case ConsoleKey.S:
							ShowStatistics(openDirectoryIndexer);
							break;
						case ConsoleKey.T:
							ShowThreads(openDirectoryIndexer);
							break;
						case ConsoleKey.J:
							SaveSession(openDirectoryIndexer);
							break;
						case ConsoleKey.U:
							SaveUrls(openDirectoryIndexer);
							break;
						case ConsoleKey.P:
							PauseAndExit(openDirectoryIndexer);
							break;
						default:
							break;
					}
				}
			}
			catch (Exception ex)
			{
				Program.Logger.Error(ex, "Error processing action");
				throw;
			}
		}
	}

	private static void SaveSession(OpenDirectoryIndexer openDirectoryIndexer)
	{
		try
		{
			string jsonPath = Library.GetOutputFullPath(OpenDirectoryIndexer.Session, openDirectoryIndexer.OpenDirectoryIndexerSettings, "json");

			Program.Logger.Information("Saving session to JSON..");
			Console.WriteLine("Saving session to JSON..");

			openDirectoryIndexer.SaveSessionJson(jsonPath);

			Program.Logger.Information("Saved session to JSON: {path}", jsonPath);
			Console.WriteLine($"Saved session to JSON: {jsonPath}");
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error saving session to JSON");
		}
	}

	private static void SaveUrls(OpenDirectoryIndexer openDirectoryIndexer)
	{
		try
		{
			Program.Logger.Information("Saving URL list to file..");
			Console.WriteLine("Saving URL list to file..");

			List<string> distinctUrls = openDirectoryIndexer.GetDistinctFileUrls();
			List<string> outputUrls = [];
			foreach (string url in distinctUrls)
			{
				string safeUrl = url.Contains("#") ? url.Replace("#", "%23") : url;
				if (Uri.TryCreate(safeUrl, UriKind.Absolute, out Uri uri))
				{
					outputUrls.Add(uri.AbsoluteUri);
				}
				else
				{
					outputUrls.Add(safeUrl);
				}
			}
			string urlsPath = Library.GetOutputFullPath(OpenDirectoryIndexer.Session, openDirectoryIndexer.OpenDirectoryIndexerSettings, "txt");
			File.WriteAllLines(urlsPath, outputUrls);

			Program.Logger.Information("Saved URL list to file: {path}", urlsPath);
			Console.WriteLine($"Saved URL list to file: {urlsPath}");
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error saving URLs to file");
		}
	}

	/// <summary>
	/// Stops the scan cleanly instead of requiring a hard kill to interrupt one (issue #56 phase 6, the
	/// 'P' key). Waits for OpenDirectoryIndexer.PauseAsync to make sure the scan database (if any) is
	/// safely flushed before exiting, then prints the command to pick the scan back up with --resume.
	/// </summary>
	private static void PauseAndExit(OpenDirectoryIndexer openDirectoryIndexer)
	{
		// Captured before pausing: ScanDatabase is disposed (and nulled out) as part of PauseAsync.
		string dbPath = openDirectoryIndexer.ScanDatabase?.Path;

		try
		{
			if (dbPath is null)
			{
				Console.WriteLine("Pausing without --use-database: this scan cannot be resumed. Exiting.");
				Program.Logger.Warning("Paused without --use-database; scan cannot be resumed.");
			}
			else
			{
				Console.WriteLine("Pausing - waiting for in-flight requests and saving the database, please wait...");
			}

			openDirectoryIndexer.PauseAsync().GetAwaiter().GetResult();

			if (dbPath is not null)
			{
				Console.WriteLine($"Paused. Resume with: --resume --use-database --db-path \"{dbPath}\" -u {OpenDirectoryIndexer.Session.Root.Url}");
			}
		}
		catch (Exception ex)
		{
			Program.Logger.Error(ex, "Error pausing");
		}
		finally
		{
			KillApplication();
		}
	}

	public static void KillApplication()
	{
		Console.WriteLine("Exiting...");
		Program.Logger.Information("Exiting...");
		Environment.Exit(0);
	}

	private static void ShowThreads(OpenDirectoryIndexer openDirectoryIndexer)
	{
		Console.WriteLine($"Running threads:");

		lock (openDirectoryIndexer.WebDirectoryProcessorInfoLock)
		{
			foreach (KeyValuePair<string, WebDirectory> webDirectory in openDirectoryIndexer.WebDirectoryProcessorInfo.OrderByDescending(x => x.Value.StartTime))
			{
				Console.WriteLine($"[{webDirectory.Key}] {Library.FormatWithThousands((DateTimeOffset.UtcNow - webDirectory.Value.StartTime).TotalMilliseconds)}ms | {webDirectory.Value.Url}");
			}
		}
	}

	private static void ShowStatistics(OpenDirectoryIndexer openDirectoryIndexer)
	{
		Console.WriteLine(Statistics.GetSessionStats(OpenDirectoryIndexer.Session, includeExtensions: true, scanDatabase: openDirectoryIndexer.ScanDatabase));
		Console.WriteLine($"Queue: {Library.FormatWithThousands(openDirectoryIndexer.WebDirectoriesQueue.Count)} ({openDirectoryIndexer.RunningWebDirectoryThreads} threads), Queue (filesizes): {Library.FormatWithThousands(openDirectoryIndexer.WebFilesFileSizeQueue.Count)} ({openDirectoryIndexer.RunningWebFileFileSizeThreads} threads)");
	}

	/// <summary>
	/// Sets to clipboard to the suplied string.
	/// Attempts in order:
	///	The builtin Clipboard.SetText() method, works on Windows and linux X11 (using xclip)
	/// wl-copy, works on linux with wayland wlroots (sway and many others)
	/// </summary>
	/// <param name="value">String to set the clipboard to.</param>
	private static void SetClipboard(string value)
	{
		if (value == null)
		{
			throw new ArgumentNullException(nameof(value), "Attempt to set clipboard with null");
		}

		try
		{
			new Clipboard().SetText(value);

			return;
		}
		catch
		{
		}

		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return;
		}

		try
		{
			Process clipboardExecutable = new()
			{
				StartInfo = new ProcessStartInfo
				{
					RedirectStandardInput = true,
					FileName = @"wl-copy",
				}
			};

			clipboardExecutable.Start();
			clipboardExecutable.StandardInput.Write(value);
			clipboardExecutable.StandardInput.Close();

			return;
		}
		catch
		{
		}
	}
}
