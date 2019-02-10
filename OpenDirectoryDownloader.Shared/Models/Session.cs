using Newtonsoft.Json;
using Roslyn.Utilities;
using System;
using System.Collections.Generic;

namespace OpenDirectoryDownloader.Shared.Models
{
    public class Session
    {
        public WebDirectory Root { get; set; }
        public DateTimeOffset Started { get; set; }
        public DateTimeOffset Finished { get; set; }
        public Dictionary<int, int> HttpStatusCodes { get; set; } = new Dictionary<int, int>();
        public long TotalHttpTraffic { get; set; }
        public int TotalHttpRequests { get; set; }
        public int TotalFiles { get; set; }
        public long TotalFileSizeEstimated { get; set; }
        public int Errors { get; set; }
        public int Skipped { get; set; }
        [JsonIgnore]
        public bool StopLogging { get; set; }
        public string UploadedUrlsUrl { get; set; }
        public List<string> UrlsWithErrors { get; set; } = new List<string>();
        [JsonIgnore]
        public ConcurrentSet<string> ProcessedUrls { get; set; } = new ConcurrentSet<string>();
        public SpeedtestResult SpeedtestResult { get; set; }
    }
}
