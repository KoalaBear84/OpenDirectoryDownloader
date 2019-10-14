using NLog;
using OpenDirectoryDownloader.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using TextCopy;

namespace OpenDirectoryDownloader
{
    public class Command
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Set console properties (Window size)
        /// </summary>
        internal static void SetConsoleProperties()
        {
            Console.WindowWidth = Math.Min(170, Console.LargestWindowWidth);
            Console.WindowHeight = Math.Min(34, Console.LargestWindowHeight);
        }

        internal static void ShowInfoAndCommands()
        {
            Console.WriteLine(
                "****************************************************************************\n" +
                "****************************************************************************\n" +
                "***  Press I for info (this)                                             ***\n" +
                "***  Press S for statistics                                              ***\n" +
                "***  Press T for thread info                                             ***\n" +
                "***  Press J for Save JSON                                               ***\n" +
                "***                                                                      ***\n" +
                "***  Press ESC to EXIT                                                   ***\n" +
                "****************************************************************************\n" +
                "****************************************************************************"
            );
        }

        internal static void ProcessConsoleInput(OpenDirectoryIndexer openDirectoryIndexer)
        {
            while (true)
            {
                try
                {
                    switch (Console.ReadKey(true).Key)
                    {
                        case ConsoleKey.Escape:
                            KillApplication();
                            break;
                        case ConsoleKey.I:
                            ShowInfoAndCommands();
                            break;
                        case ConsoleKey.C:
                            if (openDirectoryIndexer.Session.Finished != DateTimeOffset.MinValue)
                            {
                                Clipboard.SetText(Statistics.GetSessionStats(openDirectoryIndexer.Session, includeExtensions: true, onlyRedditStats: true));
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
                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error processing action");
                }
            }
        }

        private static void SaveSession(OpenDirectoryIndexer openDirectoryIndexer)
        {
            try
            {
                Console.WriteLine("Saving session to JSON");
                Library.SaveSessionJson(openDirectoryIndexer.Session);
                Console.WriteLine("Saved session to JSON");
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        public static void KillApplication()
        {
            Console.WriteLine("Exiting...");
            Logger.Info("Exiting...");
            Environment.Exit(1);
        }

        private static void ShowThreads(OpenDirectoryIndexer openDirectoryIndexer)
        {
            Console.WriteLine($"Running threads:");

            lock (openDirectoryIndexer.WebDirectoryProcessorInfoLock)
            {
                foreach (KeyValuePair<string, WebDirectory> webDirectory in openDirectoryIndexer.WebDirectoryProcessorInfo.OrderBy(i => i.Key))
                {
                    Console.WriteLine($"[{webDirectory.Key}] {Library.FormatWithThousands((DateTimeOffset.UtcNow - webDirectory.Value.StartTime).TotalMilliseconds)}ms | {webDirectory.Value.Url}");
                }
            }
        }

        private static void ShowStatistics(OpenDirectoryIndexer openDirectoryIndexer)
        {
            Console.WriteLine(Statistics.GetSessionStats(openDirectoryIndexer.Session, includeExtensions: true));
            Console.WriteLine($"Queue: {Library.FormatWithThousands(openDirectoryIndexer.WebDirectoriesQueue.Count)}, Threads: {openDirectoryIndexer.RunningWebDirectoryThreads}");
            Console.WriteLine($"Queue (filesize): {Library.FormatWithThousands(openDirectoryIndexer.WebFilesFileSizeQueue.Count)}, Threads (filesize): {openDirectoryIndexer.RunningWebFileFileSizeThreads}");
        }
    }
}
