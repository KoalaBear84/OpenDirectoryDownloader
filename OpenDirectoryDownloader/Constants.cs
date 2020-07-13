namespace OpenDirectoryDownloader
{
    public class Constants
    {
        public const string GoogleDriveDomain = "drive.google.com";
        public const string BlitzfilesTechDomain = "blitzfiles.tech";
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
        public const string Parameters_Password = "PASSWORD";
        public const string Parameters_GdIndex_RootId = "GdIndex_RootId";
        public const string Parameters_FtpEncryptionMode = "FtpEncryptionMode";

        public class UserAgent
        {
            public const string Curl = "curl/7.55.1";
            public const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3800.0 Safari/537.36";
        }

        public class UriScheme
        {
            public const string Http = "http";
            public const string Https = "https";
            public const string Ftp = "ftp";
            public const string Ftps = "ftps";
        }
    }
}
