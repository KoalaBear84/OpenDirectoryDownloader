using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDirectoryDownloader.Outputs.FileUpload;

// Source generated, so the trimmed release keeps what is needed. One context per set of options the calls used.

/// <summary>The plain JsonSerializer defaults: the responses read as text, and the uploaded files written to the history</summary>
[JsonSerializable(typeof(AnonFilesFile))]
[JsonSerializable(typeof(QuAxResponse))]
[JsonSerializable(typeof(GoFileIoFile))]
[JsonSerializable(typeof(GoFileIoUploadFileResponse))]
[JsonSerializable(typeof(UploadFilesIoFile))]
internal partial class FileUploadJsonContext : JsonSerializerContext;

/// <summary>The web defaults ReadFromJsonAsync and GetFromJsonAsync use</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GoFileIoServersResponse))]
[JsonSerializable(typeof(GoFileIoAccountsResponse))]
[JsonSerializable(typeof(GoFileIoAccountResponse))]
[JsonSerializable(typeof(GoFileIoFolderResponse))]
[JsonSerializable(typeof(GoFileIoUploadFileResponse))]
internal partial class FileUploadWebJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UploadFilesIoFile))]
internal partial class FileUploadCaseInsensitiveJsonContext : JsonSerializerContext;
