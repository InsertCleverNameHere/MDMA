using Microsoft.Data.Sqlite;

namespace Mdma.Core;

/// <summary>
/// Exports an NDM task to a .mdma package. Reads segment byte-range structure
/// from segments.bin (24-byte records per docs/ndm.md §4.2), packages each
/// seg.xN file as a chunk, and pulls headers + mimetype from neatdb.db (not
/// present on DownloadTaskSummary, which only carries the normalized
/// scan-display fields).
/// </summary>
public sealed class NdmExporter : IMdmaExporter
{
    private const int SegmentRecordSize = 24;

    public TargetApp SourceApp => TargetApp.NDM;

    public Result<string> Export(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        WorkingRoot workingRoot,
        string destinationMdmaPath,
        IProgress<OperationProgress>? progress = null
    )
    {
        if (sourceLocation.InstallOrConfigDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "No temp directory is known for this NDM location -- cannot locate segment files.",
                Details: "TargetAppLocation.InstallOrConfigDir was null."
            );
        }

        if (sourceLocation.MetadataDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "No metadata directory (neatdb.db location) is known for this NDM location.",
                Details: "TargetAppLocation.MetadataDir was null."
            );
        }

        var taskDir = Path.Combine(sourceLocation.InstallOrConfigDir, task.NativeId);
        var segmentsBinPath = Path.Combine(taskDir, "segments.bin");
        if (!File.Exists(segmentsBinPath))
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "segments.bin was not found for this task.",
                Details: segmentsBinPath
            );
        }

        progress?.Report(new OperationProgress("Reading segment structure", null, null));

        List<(int SegmentId, long StartByte, long EndByte)> segments;
        try
        {
            segments = ReadSegmentsBin(segmentsBinPath);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "Failed to parse segments.bin.",
                Details: segmentsBinPath,
                Inner: ex
            );
        }

        var chunkSources = new List<MdmaChunkSource>();
        foreach (var (segmentId, startByte, endByte) in segments)
        {
            var segFilePath = Path.Combine(taskDir, $"seg.x{segmentId}");
            if (!File.Exists(segFilePath))
            {
                return new MdmaError(
                    MdmaErrorCode.ExportFailed,
                    $"Segment data file seg.x{segmentId} was listed in segments.bin but not found on disk.",
                    Details: segFilePath
                );
            }
            chunkSources.Add(new MdmaChunkSource(segmentId, startByte, endByte, segFilePath));
        }

        progress?.Report(new OperationProgress("Reading task metadata", null, null));

        string? mimeType;
        List<KeyValuePair<string, string>> headers;
        try
        {
            (mimeType, headers) = ReadMetadata(sourceLocation.MetadataDir, task.NativeId);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "Failed to read task metadata from neatdb.db.",
                Details: sourceLocation.MetadataDir,
                Inner: ex
            );
        }

        progress?.Report(new OperationProgress("Writing .mdma package", null, null));

        var writer = new MdmaPackageWriter();
        return writer.WritePackage(
            TargetApp.NDM,
            task.Url,
            task.Filename,
            task.TotalBytes,
            mimeType,
            headers,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            chunkSources,
            destinationMdmaPath
        );
    }

    private static List<(int SegmentId, long StartByte, long EndByte)> ReadSegmentsBin(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length % SegmentRecordSize != 0)
        {
            throw new InvalidDataException(
                $"segments.bin length ({bytes.Length}) is not a multiple of {SegmentRecordSize}."
            );
        }

        var results = new List<(int, long, long)>();
        for (int offset = 0; offset < bytes.Length; offset += SegmentRecordSize)
        {
            var segmentId = BitConverter.ToUInt16(bytes, offset);
            // bytes[offset+2..4) = segment_index (unused here)
            // bytes[offset+4..8) = next_segment_id (unused here -- chunk ordering
            // is derived from segment_id itself, not the linked-list pointer)
            var startByte = (long)BitConverter.ToUInt64(bytes, offset + 8);
            var endByte = (long)BitConverter.ToUInt64(bytes, offset + 16);
            results.Add((segmentId, startByte, endByte));
        }

        return results;
    }

    private static (string? MimeType, List<KeyValuePair<string, string>> Headers) ReadMetadata(
        string metadataDir,
        string nativeId
    )
    {
        var dbPath = Path.Combine(metadataDir, "neatdb.db");
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();

        string? mimeType = null;
        var mimeCmd = conn.CreateCommand();
        mimeCmd.CommandText = "SELECT mimetype FROM downloads WHERE id = $id;";
        mimeCmd.Parameters.AddWithValue("$id", long.Parse(nativeId));
        var mimeResult = mimeCmd.ExecuteScalar();
        if (mimeResult is not null and not DBNull)
        {
            mimeType = mimeResult.ToString();
        }

        var headers = new List<KeyValuePair<string, string>>();
        var headersCmd = conn.CreateCommand();
        headersCmd.CommandText = "SELECT header FROM headers WHERE id = $id;";
        headersCmd.Parameters.AddWithValue("$id", long.Parse(nativeId));
        using (var reader = headersCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var raw = reader.GetString(0); // e.g. "Referer: https://example.com"
                var separatorIndex = raw.IndexOf(": ", StringComparison.Ordinal);
                if (separatorIndex > 0)
                {
                    headers.Add(
                        new KeyValuePair<string, string>(
                            raw[..separatorIndex],
                            raw[(separatorIndex + 2)..]
                        )
                    );
                }
            }
        }

        SqliteConnection.ClearPool(conn);
        return (mimeType, headers);
    }
}
