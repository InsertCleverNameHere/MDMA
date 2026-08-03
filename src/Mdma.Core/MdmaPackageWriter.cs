using System.IO.Compression;
using System.Text.Json;

namespace Mdma.Core;

/// <summary>One chunk's source data for packaging: its structural byte range
/// (from the source app's own segment/chunk metadata) plus the path to the
/// already-staged file containing its actual downloaded bytes.</summary>
public sealed record MdmaChunkSource(int Index, long StartByte, long EndByte, string FilePath);

/// <summary>
/// Builds a .mdma package: manifest.json + data/chunk_*.bin + checksum.sha256,
/// per architecture.md §5. downloaded_bytes for each chunk is taken from the
/// ACTUAL file length at FilePath, not from any caller-supplied value, so the
/// manifest can never disagree with the bytes actually being packaged.
/// </summary>
public sealed class MdmaPackageWriter
{
    public Result<string> WritePackage(
        TargetApp origin,
        string url,
        string filename,
        long totalBytes,
        string? mimeType,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        long createdEpochMillis,
        IReadOnlyList<MdmaChunkSource> chunks,
        string destinationZipPath)
    {
        foreach (var chunk in chunks)
        {
            if (!File.Exists(chunk.FilePath))
            {
                return new MdmaError(
                    MdmaErrorCode.Unknown,
                    "A chunk source file was not found while building the .mdma package.",
                    Details: $"chunk {chunk.Index}: {chunk.FilePath}");
            }
        }

        var destinationDir = Path.GetDirectoryName(Path.GetFullPath(destinationZipPath));
        if (!string.IsNullOrEmpty(destinationDir))
        {
            try { Directory.CreateDirectory(destinationDir); }
            catch (Exception ex)
            {
                return new MdmaError(MdmaErrorCode.Unknown, "Could not create destination directory.", Details: destinationDir, Inner: ex);
            }
        }

        var chunkHashes = new Dictionary<string, string>();
        var manifestChunks = new List<MdmaChunkDto>();

        try
        {
            using var zipStream = new FileStream(destinationZipPath, FileMode.Create);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

            foreach (var chunk in chunks.OrderBy(c => c.Index))
            {
                var entry = zip.CreateEntry($"data/chunk_{chunk.Index}.bin", CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                using (var sourceStream = File.OpenRead(chunk.FilePath))
                {
                    sourceStream.CopyTo(entryStream);
                }

                var hash = MdmaChecksumHelper.ComputeFileHash(chunk.FilePath);
                chunkHashes[chunk.Index.ToString()] = hash;

                manifestChunks.Add(new MdmaChunkDto
                {
                    Index = chunk.Index,
                    StartByte = chunk.StartByte,
                    EndByte = chunk.EndByte,
                    DownloadedBytes = new FileInfo(chunk.FilePath).Length,
                });
            }

            var manifest = new MdmaManifestDto
            {
                MdmaVersion = MdmaChecksumHelper.CurrentMdmaVersion,
                Origin = origin.ToString(),
                Task = new MdmaTaskDto
                {
                    Url = url,
                    Filename = filename,
                    TotalSize = totalBytes,
                    MimeType = mimeType,
                    Headers = headers.Select(h => new MdmaHeaderDto { Name = h.Key, Value = h.Value }).ToList(),
                    Created = createdEpochMillis,
                },
                Chunks = manifestChunks,
            };

            var manifestEntry = zip.CreateEntry("manifest.json");
            using (var w = new StreamWriter(manifestEntry.Open()))
            {
                w.Write(JsonSerializer.Serialize(manifest));
            }

            var checksumDto = new MdmaChecksumDto
            {
                ChunkHashes = chunkHashes,
                ManifestHash = MdmaChecksumHelper.ComputeManifestHash(
                    chunkHashes.Select(kv => new KeyValuePair<int, string>(int.Parse(kv.Key), kv.Value))),
            };

            var checksumEntry = zip.CreateEntry("checksum.sha256");
            using (var w = new StreamWriter(checksumEntry.Open()))
            {
                w.Write(JsonSerializer.Serialize(checksumDto));
            }
        }
        catch (Exception ex)
        {
            TryDeleteBestEffort(destinationZipPath);
            return new MdmaError(
                MdmaErrorCode.Unknown,
                "Failed to write the .mdma package.",
                Details: destinationZipPath,
                Inner: ex);
        }

        return Result<string>.Ok(destinationZipPath);
    }

    private static void TryDeleteBestEffort(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup of a failed write attempt */ }
    }
}
