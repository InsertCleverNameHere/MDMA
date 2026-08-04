using System.Text.Json.Serialization;

namespace Mdma.Cli;

public sealed record ErrorJsonDto(
    bool Success,
    string Code,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    string Message,
    string? Details,
    [property: JsonPropertyName("suggested_action")] string? SuggestedAction
);

public sealed record TaskJsonDto(
    string Id,
    string Source,
    string Filename,
    [property: JsonPropertyName("total_bytes")] long TotalBytes,
    [property: JsonPropertyName("downloaded_bytes")] long DownloadedBytes,
    double Percent,
    bool Resumable,
    string Status,
    string Url
);

public sealed record TaskListJsonDto(bool Success, List<TaskJsonDto> Tasks);

public sealed record BackupJsonDto(
    string Id,
    string Target,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("storage_path")] string StoragePath
);

public sealed record BackupListJsonDto(bool Success, List<BackupJsonDto> Backups);

public sealed record HelpCommandDto(string Name, string Description);

public sealed record HelpJsonDto(string App, string Description, HelpCommandDto[] Commands);

public sealed record VersionJsonDto(string App, string Version);

public sealed record CleanJsonDto(bool Success, string[] Removed, string[] Failed);

public sealed record ExportJsonDto(bool Success, string Path);

public sealed record ImportJsonDto(bool Success, string Message);

public sealed record ConvertJsonDto(bool Success, string Message);

public sealed record RevertJsonDto(bool Success, string Message);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ErrorJsonDto))]
[JsonSerializable(typeof(TaskListJsonDto))]
[JsonSerializable(typeof(BackupListJsonDto))]
[JsonSerializable(typeof(HelpJsonDto))]
[JsonSerializable(typeof(VersionJsonDto))]
[JsonSerializable(typeof(CleanJsonDto))]
[JsonSerializable(typeof(ExportJsonDto))]
[JsonSerializable(typeof(ImportJsonDto))] // <-- ADD THIS LINE
[JsonSerializable(typeof(ConvertJsonDto))]
[JsonSerializable(typeof(RevertJsonDto))]
public partial class CliJsonContext : JsonSerializerContext { }
