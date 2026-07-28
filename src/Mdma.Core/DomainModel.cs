namespace Mdma.Core;

/// <summary>Which download manager a piece of data/behavior refers to. Add new targets here.</summary>
public enum TargetApp
{
    NDM,
    JD2,
}

/// <summary>
/// Normalized, display-ready view of a single download task, regardless of source app.
/// This is what scan operations return for CLI/GUI listing — never the raw NDM/JD2 native shape.
/// </summary>
public sealed record DownloadTaskSummary(
    string NativeId,           // source app's own task id/key, opaque outside that app's injector
    TargetApp Source,
    string Filename,
    string Url,
    long TotalBytes,
    long DownloadedBytes,
    string StatusText,         // human-readable, source-app-flavored (e.g. "Paused ( 20% )")
    bool Resumable)
{
    public double PercentComplete =>
        TotalBytes <= 0 ? 0 : (double)DownloadedBytes / TotalBytes * 100.0;
}

/// <summary>One resumable byte range within a task, the universal join point between NDM's
/// segments.bin records and JD2's chunkProgress array. Every target's native chunk
/// representation should be losslessly convertible to/from this.</summary>
public sealed record ChunkRange(
    int Index,
    long StartByte,
    long EndByte,
    long DownloadedBytes);

/// <summary>Deserialized contents of manifest.json inside a .mdma package.</summary>
public sealed record MdmaManifest(
    int MdmaVersion,
    TargetApp Origin,
    string Url,
    string Filename,
    long TotalBytes,
    string? MimeType,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    long CreatedEpochMillis,
    IReadOnlyList<ChunkRange> Chunks);

/// <summary>
/// A loaded, verified .mdma package ready to hand to an injector. ChunkFilePaths are
/// staged, real files on disk (extracted from the zip into the working directory) —
/// injectors never read directly from inside the zip archive.
/// </summary>
public sealed record MdmaPackage(
    MdmaManifest Manifest,
    IReadOnlyDictionary<int, string> ChunkFilePaths, // chunk index -> staged file path
    string SourceMdmaFilePath);

/// <summary>Identifies one backup snapshot for enumeration/revert.</summary>
public sealed record BackupHandle(
    string Id,                 // e.g. "20260729T142301Z_NDM"
    TargetApp Target,
    DateTimeOffset CreatedAt,
    string StoragePath);

/// <summary>Progress callback payload for long-running operations (export/import/scan).</summary>
public sealed record OperationProgress(
    string Stage,               // e.g. "Backing up", "Writing chunks", "Verifying checksum"
    double? PercentComplete,    // null if indeterminate
    string? Detail);
