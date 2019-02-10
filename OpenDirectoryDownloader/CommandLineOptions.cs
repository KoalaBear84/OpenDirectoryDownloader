using CommandLine;

namespace OpenDirectoryDownloader
{
    public class CommandLineOptions
    {
        [Option('u', "url", Required = false, HelpText = "Url to scan")]
        public string Url { get; set; }

        [Option('q', "quit", Required = false, Default = false, HelpText = "Do not wait after scanning")]
        public bool Quit { get; set; }

        [Option('j', "no-json", Required = false, Default = false, HelpText = "Do not save JSON")]
        public bool NoJson { get; set; }

        [Option('s', "no-urls", Required = false, Default = false, HelpText = "Do not save URLs")]
        public bool NoUrls { get; set; }

        [Option('r', "no-reddit", Required = false, Default = false, HelpText = "Do not show Reddit")]
        public bool NoReddit { get; set; }

        [Option('e', "exact-file-sizes", Required = false, Default = false, HelpText = "Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)")]
        public bool ExactFileSizes { get; set; }

        [Option('p', "speed-test", Required = false, Default = true, HelpText = "Do a speed test")]
        public bool Speedtest { get; set; }

        // TODO: Future use
        //[Option('d', "download", Required = false, HelpText = "Downloads the contents (after indexing is finished)")]
        //public bool Download { get; set; }
    }
}
