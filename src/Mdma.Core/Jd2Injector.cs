using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Mdma.Core;

/// <summary>
/// Injects a loaded .mdma package into JD2, per docs/jd2.md §5 (MDMA Planned
/// Injection Specification):
///   1. Reconstruct a single sparse .part file from the package's per-chunk
///      staged data, seeking to each chunk's StartByte (mirrors JD2's own
///      RandomAccessFile.seek(offset) writing model).
///   2. Determine the next downloadList<N+1>.zip counter (highest existing + 1,
///      or 1 if none exist).
///   3. Duplicate every existing package/link entry into the new archive,
///      then add a new package entry + a new link entry for the injected task.
///
/// Unlike NDM, JD2 needs no separate external bookkeeping update (no registry
/// counter) -- its own boot logic simply picks the highest-numbered zip, so
/// creating the new file IS the complete "commit" of this injection.
/// </summary>
public sealed class Jd2Injector : IDownloadListInjector
{
    private readonly IAtomicWriter _atomicWriter;

    public TargetApp TargetApp => TargetApp.JD2;

    public Jd2Injector(IAtomicWriter atomicWriter)
    {
        _atomicWriter = atomicWriter;
    }

    public Result Inject(
        MdmaPackage package,
        TargetAppLocation destinationLocation,
        IProgress<OperationProgress>? progress = null
    )
    {
        if (destinationLocation.InstallOrConfigDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "No cfg\\ directory is known for this JD2 destination.",
                Details: "TargetAppLocation.InstallOrConfigDir was null."
            );
        }

        var downloadFolder = destinationLocation.DownloadDirectory;
        if (downloadFolder is null)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "No download folder is known for this JD2 destination (no app-level default configured).",
                Details: "TargetAppLocation.DownloadDirectory was null."
            );
        }

        var cfgDir = destinationLocation.InstallOrConfigDir;
        var existingZips = Directory.GetFiles(cfgDir, "downloadList*.zip");
        var newestZipPath = existingZips.Length > 0 ? Jd2Locator.PickNewest(existingZips) : null;
        var newCounter = existingZips.Length > 0 ? ExtractCounter(newestZipPath!) + 1 : 1;

        progress?.Report(new OperationProgress("Reconstructing part file", null, null));

        try
        {
            Directory.CreateDirectory(downloadFolder);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "Could not create the download folder.",
                Details: downloadFolder,
                Inner: ex
            );
        }

        var partFilePath = Path.Combine(downloadFolder, package.Manifest.Filename + ".part");
        var partWriteResult = _atomicWriter.WriteAtomic(
            partFilePath,
            dest => WriteSparsePartFile(dest, package)
        );
        if (!partWriteResult.IsSuccess)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "Failed to reconstruct the .part file.",
                Details: partFilePath,
                Inner: partWriteResult.Error?.Inner
            );
        }

        progress?.Report(new OperationProgress("Writing package list entry", null, null));

        var newPackageId = DetermineNewPackageId(newestZipPath).ToString();
        const string newLinkId = "00";

        var newZipPath = Path.Combine(cfgDir, $"downloadList{newCounter}.zip");
        var zipWriteResult = _atomicWriter.WriteAtomic(
            newZipPath,
            dest =>
                BuildNewArchive(
                    dest,
                    newestZipPath,
                    newPackageId,
                    newLinkId,
                    downloadFolder,
                    package.Manifest
                )
        );
        if (!zipWriteResult.IsSuccess)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "Failed to write the new downloadList zip.",
                Details: newZipPath,
                Inner: zipWriteResult.Error?.Inner
            );
        }

        return Result.Ok();
    }

    private static void WriteSparsePartFile(string destPath, MdmaPackage package)
    {
        using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        foreach (var chunk in package.Manifest.Chunks.OrderBy(c => c.Index))
        {
            if (!package.ChunkFilePaths.TryGetValue(chunk.Index, out var stagedPath))
                continue;
            var bytes = File.ReadAllBytes(stagedPath);
            if (bytes.Length == 0)
                continue;

            fs.Seek(chunk.StartByte, SeekOrigin.Begin);
            fs.Write(bytes, 0, bytes.Length);
        }
    }

    private static int DetermineNewPackageId(string? newestZipPath)
    {
        if (newestZipPath is null)
            return 0;

        using var zip = ZipFile.OpenRead(newestZipPath);
        var maxExisting = zip
            .Entries.Where(e =>
                e.Name != "extraInfo" && !e.Name.Contains('_') && int.TryParse(e.Name, out _)
            )
            .Select(e => int.Parse(e.Name))
            .DefaultIfEmpty(-1)
            .Max();

        return maxExisting + 1;
    }

    private static void BuildNewArchive(
        string destPath,
        string? sourceZipPath,
        string newPackageId,
        string newLinkId,
        string downloadFolder,
        MdmaManifest manifest
    )
    {
        using var destStream = new FileStream(destPath, FileMode.Create);
        using var destZip = new ZipArchive(destStream, ZipArchiveMode.Create);

        bool hasExtraInfo = false;

        if (sourceZipPath is not null)
        {
            using var sourceZip = ZipFile.OpenRead(sourceZipPath);
            foreach (var entry in sourceZip.Entries)
            {
                if (entry.Name == "extraInfo")
                    hasExtraInfo = true;
                var newEntry = destZip.CreateEntry(entry.FullName);
                using var sourceStream = entry.Open();
                using var newEntryStream = newEntry.Open();
                sourceStream.CopyTo(newEntryStream);
            }
        }

        if (!hasExtraInfo)
        {
            WriteJsonEntry(destZip, "extraInfo", new { version = 2 });
        }

        WriteJsonEntry(
            destZip,
            newPackageId,
            new
            {
                uid = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                name = "MDMA Injected Downloads",
                downloadFolder,
                created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                enabled = true,
            }
        );

        long totalDownloaded = manifest.Chunks.Sum(c => c.DownloadedBytes);
        string host;
        try
        {
            host = new Uri(manifest.Url).Host;
        }
        catch
        {
            host = "";
        }

        var orderedChunks = manifest.Chunks.OrderBy(c => c.Index).ToList();
        // chunkProgress as ABSOLUTE offsets, consistent with the same
        // assumption documented in Jd2Exporter: chunkProgress[i] = how far
        // (in absolute file bytes) chunk i has been written.
        var chunkProgress = orderedChunks.Select(c => c.StartByte + c.DownloadedBytes).ToArray();

        WriteJsonEntry(
            destZip,
            $"{newPackageId}_{newLinkId}",
            new
            {
                uid = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1,
                name = manifest.Filename,
                url = manifest.Url,
                host,
                size = manifest.TotalBytes,
                current = totalDownloaded,
                chunkProgress,
                availablestatus = "TRUE",
                enabled = true,
                created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                properties = new
                {
                    CHUNKS = orderedChunks.Count,
                    PROPERTY_RESUMEABLE = true,
                    URL_CONTENT = manifest.Url,
                },
            }
        );
    }

    private static void WriteJsonEntry(ZipArchive zip, string entryName, object content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(content));
    }

    private static int ExtractCounter(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}
