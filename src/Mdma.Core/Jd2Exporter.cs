using System.IO.Compression;
using System.Text.Json;

namespace Mdma.Core;

/// <summary>
/// Exports a JD2 task to a .mdma package. Unlike NDM (separate seg.xN files
/// per segment), JD2 stores everything in one sparse .part/completed file, so
/// this exporter must slice out each chunk's byte range into its own staged
/// temp file before MdmaPackageWriter can package it.
///
/// ASSUMPTION (not directly confirmed in docs/jd2.md, which only documents a
/// CHUNKS=1 example): multi-chunk files are split into CHUNKS equal-size
/// contiguous byte ranges (remainder on the last chunk), and chunkProgress[i]
/// is an ABSOLUTE file offset (matching its documented description, "last
/// successfully written byte offset") rather than a byte count relative to
/// that chunk's start. If real multi-chunk JD2 captures ever contradict this,
/// this is the method to revisit first.
/// </summary>
public sealed class Jd2Exporter : IMdmaExporter
{
    public TargetApp SourceApp => TargetApp.JD2;

    public Result<string> Export(
        DownloadTaskSummary task,
        TargetAppLocation sourceLocation,
        WorkingRoot workingRoot,
        string destinationMdmaPath,
        IProgress<OperationProgress>? progress = null)
    {
        if (sourceLocation.InstallOrConfigDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "No cfg\\ directory is known for this JD2 location.",
                Details: "TargetAppLocation.InstallOrConfigDir was null.");
        }

        var packageIdAndLink = task.NativeId.IndexOf('_');
        if (packageIdAndLink < 0)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "Task's NativeId does not match the expected <packageId>_<linkIndex> format.",
                Details: task.NativeId);
        }
        var packageId = task.NativeId[..packageIdAndLink];
        var linkId = task.NativeId[(packageIdAndLink + 1)..];

        progress?.Report(new OperationProgress("Reading task metadata", null, null));

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(sourceLocation.InstallOrConfigDir, "downloadList*.zip");
        }
        catch (Exception ex)
        {
            return new MdmaError(MdmaErrorCode.ExportFailed, "Could not enumerate downloadList*.zip files.", Details: sourceLocation.InstallOrConfigDir, Inner: ex);
        }
        if (candidates.Length == 0)
        {
            return new MdmaError(MdmaErrorCode.ExportFailed, "No downloadList*.zip file was found.", Details: sourceLocation.InstallOrConfigDir);
        }
        var newestZip = Jd2Locator.PickNewest(candidates);

        string? downloadFolder;
        long[] chunkProgress;
        int chunkCount;
        try
        {
            using var zip = ZipFile.OpenRead(newestZip);

            var packageEntry = zip.GetEntry(packageId);
            downloadFolder = packageEntry is not null ? ReadJsonString(packageEntry, "downloadFolder") : null;
            downloadFolder ??= sourceLocation.DownloadDirectory;

            var linkEntry = zip.GetEntry($"{packageId}_{linkId}");
            if (linkEntry is null)
            {
                return new MdmaError(MdmaErrorCode.ExportFailed, "Link entry not found in downloadList*.zip.", Details: task.NativeId);
            }

            using var stream = linkEntry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            chunkProgress = root.TryGetProperty("chunkProgress", out var cp) && cp.ValueKind == JsonValueKind.Array
                ? cp.EnumerateArray().Select(e => e.GetInt64()).ToArray()
                : Array.Empty<long>();

            chunkCount = root.TryGetProperty("properties", out var props) &&
                         props.TryGetProperty("CHUNKS", out var chunksProp) &&
                         chunksProp.ValueKind == JsonValueKind.Number
                ? Math.Max(1, chunksProp.GetInt32())
                : Math.Max(1, chunkProgress.Length);
        }
        catch (Exception ex)
        {
            return new MdmaError(MdmaErrorCode.ExportFailed, "Failed to read task metadata from downloadList*.zip.", Details: newestZip, Inner: ex);
        }

        if (downloadFolder is null)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "Could not determine a download folder for this task (no package downloadFolder and no app-level default).",
                Details: task.NativeId);
        }

        var partPath = Path.Combine(downloadFolder, task.Filename + ".part");
        var completePath = Path.Combine(downloadFolder, task.Filename);
        var sourceFilePath = File.Exists(partPath) ? partPath : File.Exists(completePath) ? completePath : null;
        if (sourceFilePath is null)
        {
            return new MdmaError(
                MdmaErrorCode.ExportFailed,
                "Neither the .part file nor the completed file was found for this task.",
                Details: $"{partPath} / {completePath}");
        }

        progress?.Report(new OperationProgress("Slicing chunk data", null, null));

        var stagingDir = Path.Combine(workingRoot.Path, ".mdma-tmp", $"jd2-export-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDir);
        }
        catch (Exception ex)
        {
            return new MdmaError(MdmaErrorCode.ExportFailed, "Could not create staging directory for chunk slicing.", Details: stagingDir, Inner: ex);
        }

        List<MdmaChunkSource> chunkSources;
        try
        {
            chunkSources = SliceChunks(sourceFilePath, task.TotalBytes, chunkCount, chunkProgress, stagingDir);
        }
        catch (Exception ex)
        {
            TryDeleteDirectoryBestEffort(stagingDir);
            return new MdmaError(MdmaErrorCode.ExportFailed, "Failed to slice chunk data from the source file.", Details: sourceFilePath, Inner: ex);
        }

        progress?.Report(new OperationProgress("Writing .mdma package", null, null));

        var writer = new MdmaPackageWriter();
        var writeResult = writer.WritePackage(
            TargetApp.JD2,
            task.Url,
            task.Filename,
            task.TotalBytes,
            mimeType: null, // JD2's link JSON schema documented in docs/jd2.md has no mimetype field
            headers: Array.Empty<KeyValuePair<string, string>>(), // no per-link headers table documented for JD2, unlike NDM
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            chunkSources,
            destinationMdmaPath);

        TryDeleteDirectoryBestEffort(stagingDir); // chunk bytes are now inside the .mdma; staged copies are no longer needed either way

        return writeResult;
    }

    private static List<MdmaChunkSource> SliceChunks(string sourceFilePath, long totalBytes, int chunkCount, long[] chunkProgress, string stagingDir)
    {
        var baseChunkSize = totalBytes / chunkCount;
        var sources = new List<MdmaChunkSource>();

        using var sourceStream = File.OpenRead(sourceFilePath);

        for (int i = 0; i < chunkCount; i++)
        {
            long startByte = i * baseChunkSize;
            long endByte = (i == chunkCount - 1) ? totalBytes - 1 : startByte + baseChunkSize - 1;

            long absoluteProgress = i < chunkProgress.Length ? chunkProgress[i] : startByte;
            long downloadedInChunk = Math.Clamp(absoluteProgress - startByte, 0, endByte - startByte + 1);

            var chunkFilePath = Path.Combine(stagingDir, $"chunk_{i}.bin");
            using (var chunkFileStream = File.Create(chunkFilePath))
            {
                if (downloadedInChunk > 0)
                {
                    sourceStream.Seek(startByte, SeekOrigin.Begin);
                    CopyExactly(sourceStream, chunkFileStream, downloadedInChunk);
                }
            }

            sources.Add(new MdmaChunkSource(i, startByte, endByte, chunkFilePath));
        }

        return sources;
    }

    private static void CopyExactly(Stream source, Stream destination, long byteCount)
    {
        var buffer = new byte[81920];
        long remaining = byteCount;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = source.Read(buffer, 0, toRead);
            if (read == 0) break; // source shorter than expected (sparse hole or truncated file) -- stop, don't fabricate data
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string? ReadJsonString(ZipArchiveEntry entry, string propertyName)
    {
        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.TryGetProperty(propertyName, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static void TryDeleteDirectoryBestEffort(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup of staged chunk slices */ }
    }
}
