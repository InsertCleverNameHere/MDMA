using System.Text.Json.Serialization;

namespace Mdma.Core;

public sealed record Jd2ExtraInfoDto([property: JsonPropertyName("version")] int Version);

public sealed record Jd2PackageEntryDto(
    [property: JsonPropertyName("uid")] long Uid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("downloadFolder")] string DownloadFolder,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("enabled")] bool Enabled
);

public sealed record Jd2LinkPropertiesDto(
    [property: JsonPropertyName("CHUNKS")] int Chunks,
    [property: JsonPropertyName("PROPERTY_RESUMEABLE")] bool PropertyResumeable,
    [property: JsonPropertyName("URL_CONTENT")] string UrlContent
);

public sealed record Jd2LinkEntryDto(
    [property: JsonPropertyName("uid")] long Uid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("current")] long Current,
    [property: JsonPropertyName("chunkProgress")] long[] ChunkProgress,
    [property: JsonPropertyName("availablestatus")] string Availablestatus,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("properties")] Jd2LinkPropertiesDto Properties
);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MdmaManifestDto))]
[JsonSerializable(typeof(MdmaChecksumDto))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(Jd2ExtraInfoDto))]
[JsonSerializable(typeof(Jd2PackageEntryDto))]
[JsonSerializable(typeof(Jd2LinkEntryDto))]
public partial class CoreJsonContext : JsonSerializerContext { }
