using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Mdma.Core;

/// <summary>
/// On-disk shape of manifest.json inside a .mdma package. Deliberately kept
/// separate from the domain MdmaManifest record (DomainModel.cs) so JSON
/// property naming (snake_case, nested "task" object per architecture.md)
/// doesn't leak into the domain model, and so this can evolve independently
/// as mdma_version increments.
/// </summary>
public sealed class MdmaManifestDto
{
    [JsonPropertyName("mdma_version")]
    public int MdmaVersion { get; set; }

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "";

    [JsonPropertyName("task")]
    public MdmaTaskDto Task { get; set; } = new();

    [JsonPropertyName("chunks")]
    public List<MdmaChunkDto> Chunks { get; set; } = new();
}

public sealed class MdmaTaskDto
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    [JsonPropertyName("mimetype")]
    public string? MimeType { get; set; }

    [JsonPropertyName("headers")]
    public List<MdmaHeaderDto> Headers { get; set; } = new();

    [JsonPropertyName("created")]
    public long Created { get; set; }
}

public sealed class MdmaHeaderDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

public sealed class MdmaChunkDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("start_byte")]
    public long StartByte { get; set; }

    [JsonPropertyName("end_byte")]
    public long EndByte { get; set; }

    [JsonPropertyName("downloaded_bytes")]
    public long DownloadedBytes { get; set; }
}

/// <summary>
/// On-disk shape of checksum.sha256. Despite the plain-text-sounding
/// filename (kept for continuity with architecture.md's file listing), the
/// content is small JSON implementing the locked-in hash-of-hashes scheme
/// (mdma-core-plan.md Decision #2): every chunk file gets its own SHA-256,
/// and manifest_hash covers the ordered list of those hashes, so a loader
/// can report exactly which chunk is corrupt rather than just "mismatch".
/// </summary>
public sealed class MdmaChecksumDto
{
    /// <summary>Chunk index (as string, since JSON object keys are strings) -> uppercase hex SHA-256.</summary>
    [JsonPropertyName("chunk_hashes")]
    public Dictionary<string, string> ChunkHashes { get; set; } = new();

    [JsonPropertyName("manifest_hash")]
    public string ManifestHash { get; set; } = "";
}

/// <summary>Shared hashing helpers so MdmaPackageWriter and MdmaLoader (and
/// test fixtures) compute hashes identically.</summary>
public static class MdmaChecksumHelper
{
    public const int CurrentMdmaVersion = 1;

    public static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    /// <summary>Computes the hash-of-hashes over chunk hashes ordered by index
    /// (ascending), joined with '\n'. Both writer and loader MUST use this
    /// exact method so their outputs are directly comparable.</summary>
    public static string ComputeManifestHash(IEnumerable<KeyValuePair<int, string>> chunkHashesByIndex)
    {
        var ordered = chunkHashesByIndex.OrderBy(kv => kv.Key).Select(kv => kv.Value);
        var joined = string.Join("\n", ordered);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(joined)));
    }
}
