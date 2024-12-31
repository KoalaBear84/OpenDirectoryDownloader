namespace OpenDirectoryDownloader;

public class Constants
{
	public const string AmazonS3Domain = "s3.amazonaws.com";
	public const string BlitzfilesTechDomain = "blitzfiles.tech";
	public const string DropboxDomain = "www.dropbox.com";
	public const string GitHubDomain = "github.com";
	public const string GitHubApiDomain = "api.github.com";
	public const string GoFileIoDomain = "gofile.io";
	public const string GoogleDriveDomain = "drive.google.com";
	public const string MediafireDomain = "www.mediafire.com";
	public const string PixeldrainDomain = "pixeldrain.com";

	public const string Parameters_GdIndex_RootId = "GDINDEX_ROOTID";
	public const string Parameters_GoFileIOAccountToken = "GOFILE_ACCOUNTTOKEN";

	public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
	public const string Parameters_Password = "PASSWORD";
	public const string Parameters_FtpEncryptionMode = "FtpEncryptionMode";

	public const string GoogleDriveIndexType = "GOOGLEDRIVEINDEXTYPE";

	public const string Root = "ROOT";
	public const string Ftp_Max_Connections = "MAX_CONNECTIONS";
	public const int Kilobyte = 1024;
	public const int Megabyte = 1024 * Kilobyte;

	public class UserAgent
	{
		public const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
		public const string Curl = "curl/7.55.1";
	}

	public class UriScheme
	{
		public const string Http = "http";
		public const string Https = "https";
		public const string Ftp = "ftp";
		public const string Ftps = "ftps";
	}
}
