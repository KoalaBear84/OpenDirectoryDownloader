using CommandLine;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpenDirectoryDownloader
{
    class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static CommandLineOptions CommandLineOptions { get; set; }

        static async Task Main(string[] args)
        {
            Console.Title = $"OpenDirectoryDownloader";

            Console.WriteLine("Started");
            Logger.Info("Started");

            Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithNotParsed(o =>
                {
                    List<Error> errors = o.ToList();

                    if (errors.Any())
                    {
                        foreach (Error error in errors)
                        {
                            Console.WriteLine($"Error command line parameter '{error.Tag}'");
                        }
                    }
                })
                .WithParsed(o => CommandLineOptions = o);

            string url = CommandLineOptions.Url;

            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("Which URL do you want to index?");
                url = Console.ReadLine();
            }

            OpenDirectoryIndexerSettings openDirectoryIndexerSettings = new OpenDirectoryIndexerSettings
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

                url = Library.FixUrl(url);
                Console.WriteLine($"URL fixed: {url}");

                openDirectoryIndexerSettings.Url = url;
            }

            openDirectoryIndexerSettings.Threads = 50;

            // FTP
            // TODO: Make dynamic
            if (openDirectoryIndexerSettings.Url?.StartsWith("ftp") == true)
            {
                openDirectoryIndexerSettings.Threads = 6;
            }

            OpenDirectoryIndexer openDirectoryIndexer = new OpenDirectoryIndexer(openDirectoryIndexerSettings);

            Console.Title = $"{new Uri(url).Host.Replace("www.", string.Empty)} - {Console.Title}";

            openDirectoryIndexer.StartIndexingAsync();
            Console.WriteLine("Started indexing!");

            Command.ShowInfoAndCommands();
            Command.ProcessConsoleInput(openDirectoryIndexer);

            await openDirectoryIndexer.IndexingTask;

            if (!CommandLineOptions.Quit)
            {
                Console.WriteLine("Press ESC to exit");
                Console.ReadKey();
            }
        }
    }
}
