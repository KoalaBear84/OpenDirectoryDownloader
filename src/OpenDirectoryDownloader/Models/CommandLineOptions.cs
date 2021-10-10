using CommandLine;

namespace OpenDirectoryDownloader.Models
{
    public class CommandLineOptions
    {
        [Option('u', "url", Required = false, HelpText = "Url to scan")]
        public string Url { get; set; }

        [Option('t', "threads", Required = false, Default = 5, HelpText = "Number of threads")]
        public int Threads { get; set; }

        [Option('o', "timeout", Required = false, Default = 100, HelpText = "Number of seconds for timeout")]
        public int Timeout { get; set; }

        [Option('q', "quit", Required = false, Default = false, HelpText = "Do not wait after scanning")]
        public bool Quit { get; set; }

        [Option('c', "clipboard", Required = false, Default = false, HelpText = "Copy Reddit stats after scanning")]
        public bool Clipboard { get; set; }

        [Option('j', "json", Required = false, Default = false, HelpText = "Save JSON file")]
        public bool Json { get; set; }

        [Option('f', "no-urls", Required = false, Default = false, HelpText = "Do not save URLs file")]
        public bool NoUrls { get; set; }

        [Option('r', "no-reddit", Required = false, Default = false, HelpText = "Do not show Reddit stats markdown")]
        public bool NoReddit { get; set; }

        [Option('e', "exact-file-sizes", Required = false, Default = false, HelpText = "Exact file sizes (WARNING: Uses HEAD requests which takes more time and is heavier for server)")]
        public bool ExactFileSizes { get; set; }

        [Option('l', "upload-urls", Required = false, Default = false, HelpText = "Uploads urls file")]
        public bool UploadUrls { get; set; }

        [Option('s', "speedtest", Required = false, Default = false, HelpText = "Do a speed test")]
        public bool Speedtest { get; set; }

        [Option('a', "user-agent", Required = false, HelpText = "Use custom default User Agent")]
        public string UserAgent { get; set; }

        [Option("username", Required = false, Default = "", HelpText = "Username")]
        public string Username { get; set; }

        [Option("password", Required = false, Default = "", HelpText = "Password")]
        public string Password { get; set; }

        [Option("output-file", Required = false, Default = null, HelpText = "Save output files to specific base filename")]
        public string OutputFile { get; set; }

        [Option("fast-scan", Required = false, Default = false, HelpText = "Only perform actions that are fast, so no HEAD requests, etc. Might result in missing file sizes")]
        public bool FastScan { get; set; }

        [Option("proxy-address", Required = false, Default = "", HelpText = "Proxy address, like: socks5://127.0.0.1:9050")]
        public string ProxyAddress { get; set; }

        [Option("proxy-username", Required = false, Default = "", HelpText = "Proxy username")]
        public string ProxyUsername { get; set; }

        [Option("proxy-password", Required = false, Default = "", HelpText = "Proxy password")]
        public string ProxyPassword { get; set; }

        // TODO: Future use
        //[Option('d', "download", Required = false, HelpText = "Downloads the contents (after indexing is finished)")]
        //public bool Download { get; set; }
    }
}
