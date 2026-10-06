using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Storage;

[JsonSerializable(typeof(Dictionary<int, int>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
