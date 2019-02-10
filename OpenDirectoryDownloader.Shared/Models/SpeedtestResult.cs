namespace OpenDirectoryDownloader.Shared
{
    public class SpeedtestResult
    {
        public long DownloadedBytes { get; set; }
        public double DownloadedMBs => DownloadedBytes / 1024d / 1024d;
        public long ElapsedMiliseconds { get; set; }
        public double MBsPerSecond => DownloadedMBs / (ElapsedMiliseconds / 1000d);
        public double MbitsPerSecond => DownloadedMBs / (ElapsedMiliseconds / 1000d) * 8;
        public double MaxMBsPerSecond { get; set; }
    }
}
