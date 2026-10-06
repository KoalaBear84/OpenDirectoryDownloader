using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Server;

internal sealed record ErrorResponse(string Error);

/// <summary>A server-sent event with the progress of a ZIP download</summary>
internal sealed record ZipProgressEvent(long FilesDone, long TotalFiles, long BytesWritten, string CurrentFile, long Skipped, bool IsComplete, string Error);

/// <summary>Source generated, so the trimmed release keeps what is needed</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ZipProgressEvent))]
internal partial class ViewerJsonContext : JsonSerializerContext;
