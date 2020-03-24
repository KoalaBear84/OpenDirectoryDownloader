using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OpenDirectoryDownloader.Shared.Models
{
    [DebuggerDisplay("{Name,nq}, Directories: {Subdirectories.Count,nq}, Files: {Files.Count,nq}")]
    public class WebDirectory
    {
        public WebDirectory(WebDirectory parentWebDirectory)
        {
            ParentDirectory = parentWebDirectory;
        }

        [JsonIgnore]
        public WebDirectory ParentDirectory { get; set; }

        public string Url { get; set; }

        [JsonIgnore]
        public Uri Uri => new Uri(Url);

        public string Name { get; set; }

        public string Description { get; set; }

        public bool Finished { get; set; }

        [JsonIgnore]
        public bool ParsedSuccesfully { get; set; }

        public ConcurrentList<WebDirectory> Subdirectories { get; set; } = new ConcurrentList<WebDirectory>();

        public List<WebFile> Files { get; set; } = new List<WebFile>();

        public bool Error { get; set; }

        [JsonIgnore]
        public long TotalFileSize => Subdirectories.Sum(sd => sd.TotalFileSize) + Files.Sum(f => f.FileSize);

        [JsonIgnore]
        public int TotalFiles => Subdirectories.Sum(sd => sd.TotalFiles) + Files.Count;

        [JsonIgnore]
        public int TotalDirectories => Subdirectories.Sum(sd => sd.TotalDirectories) + Subdirectories.Count(sd => sd.Finished);

        [JsonIgnore]
        public int TotalDirectoriesIncludingUnfinished => Subdirectories.Sum(sd => sd.TotalDirectories) + Subdirectories.Count;

        [JsonIgnore]
        public IEnumerable<string> Urls => Files.Select(f => f.Url);

        [JsonIgnore]
        public IEnumerable<string> AllFileUrls => Subdirectories.SelectMany(sd => sd.AllFileUrls).Concat(Urls).OrderBy(url => url);

        [JsonIgnore]
        public IEnumerable<WebFile> AllFiles => Subdirectories.SelectMany(sd => sd.AllFiles).Concat(Files);

        [JsonIgnore]
        public string Parser { get; set; } = string.Empty;

        [JsonIgnore]
        public DateTimeOffset StartTime { get; set; }
        [JsonIgnore]
        public DateTimeOffset FinishTime { get; set; }

        [JsonIgnore]
        public int HeaderCount { get; set; }
    }
}