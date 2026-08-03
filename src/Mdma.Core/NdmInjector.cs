using Microsoft.Data.Sqlite;

namespace Mdma.Core;

/// <summary>
/// Injects a loaded .mdma package into NDM, per docs/ndm.md §6 (MDMA Planned
/// Injection Specification):
///   1. Compute NewTaskID = LastDownloadID + 1 from the registry.
///   2. Create <TempDirectory>\<NewTaskID>\, write seg.xN files + synthesized
///      segments.bin.
///   3. Insert a row into neatdb.db's downloads/headers tables (via atomic
///      copy-modify-swap of the whole db file, since it's a single file).
///   4. Update LastDownloadID in the registry -- LAST, only after the DB
///      insert has succeeded, matching architecture.md's ordering.
///
/// Known limitation (same class of issue as RevertManager's deferred
/// multi-file rollback, mdma-core-plan.md Phase 3.2): if the registry update
/// in step 4 fails after step 3 already succeeded, the DB row is not rolled
/// back. This is a genuine cross-store atomicity gap; flagged rather than
/// silently accepted.
/// </summary>
public sealed class NdmInjector : IDownloadListInjector
{
    private const string RegistryKeyPath = @"SOFTWARE\NeatDM";
    private const string LastDownloadIdValue = "LastDownloadID";

    private readonly IRegistryAccessor _registry;
    private readonly IAtomicWriter _atomicWriter;

    public TargetApp TargetApp => TargetApp.NDM;

    public NdmInjector(IRegistryAccessor registry, IAtomicWriter atomicWriter)
    {
        _registry = registry;
        _atomicWriter = atomicWriter;
    }

    public Result Inject(MdmaPackage package, TargetAppLocation destinationLocation, IProgress<OperationProgress>? progress = null)
    {
        if (destinationLocation.InstallOrConfigDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "No temp directory is known for this NDM destination.",
                Details: "TargetAppLocation.InstallOrConfigDir was null.");
        }
        if (destinationLocation.MetadataDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "No metadata directory (neatdb.db location) is known for this NDM destination.",
                Details: "TargetAppLocation.MetadataDir was null.");
        }

        var lastId = _registry.ReadDword(RegistryKeyPath, LastDownloadIdValue) ?? 0;
        var newTaskId = lastId + 1;

        progress?.Report(new OperationProgress("Creating task directory", null, null));

        var taskDir = Path.Combine(destinationLocation.InstallOrConfigDir, newTaskId.ToString());
        if (Directory.Exists(taskDir))
        {
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "A task directory already exists at the computed new task ID -- refusing to overwrite.",
                Details: taskDir);
        }

        try
        {
            Directory.CreateDirectory(taskDir);
        }
        catch (Exception ex)
        {
            return new MdmaError(MdmaErrorCode.InjectionFailed, "Could not create the task directory.", Details: taskDir, Inner: ex);
        }

        progress?.Report(new OperationProgress("Writing segment files", null, null));

        var orderedChunks = package.Manifest.Chunks.OrderBy(c => c.Index).ToList();
        foreach (var chunk in orderedChunks)
        {
            if (!package.ChunkFilePaths.TryGetValue(chunk.Index, out var stagedPath))
            {
                TryDeleteDirectoryBestEffort(taskDir);
                return new MdmaError(MdmaErrorCode.InjectionFailed, $"No staged file found for chunk {chunk.Index}.", Details: package.SourceMdmaFilePath);
            }

            var destSegPath = Path.Combine(taskDir, $"seg.x{chunk.Index}");
            var bytes = File.ReadAllBytes(stagedPath);
            var writeResult = _atomicWriter.WriteAtomic(destSegPath, dest => File.WriteAllBytes(dest, bytes));
            if (!writeResult.IsSuccess)
            {
                TryDeleteDirectoryBestEffort(taskDir);
                return new MdmaError(MdmaErrorCode.InjectionFailed, $"Failed to write seg.x{chunk.Index}.", Details: destSegPath, Inner: writeResult.Error?.Inner);
            }
        }

        var segmentsBinPath = Path.Combine(taskDir, "segments.bin");
        var segmentsBinResult = _atomicWriter.WriteAtomic(segmentsBinPath, dest => WriteSegmentsBin(dest, orderedChunks));
        if (!segmentsBinResult.IsSuccess)
        {
            TryDeleteDirectoryBestEffort(taskDir);
            return new MdmaError(MdmaErrorCode.InjectionFailed, "Failed to write segments.bin.", Details: segmentsBinPath, Inner: segmentsBinResult.Error?.Inner);
        }

        progress?.Report(new OperationProgress("Updating neatdb.db", null, null));

        var dbPath = Path.Combine(destinationLocation.MetadataDir, "neatdb.db");
        var dbResult = _atomicWriter.WriteAtomic(dbPath, dest => InsertIntoDatabase(
            dbPath, dest, newTaskId, package.Manifest, destinationLocation));
        if (!dbResult.IsSuccess)
        {
            TryDeleteDirectoryBestEffort(taskDir);
            return new MdmaError(MdmaErrorCode.InjectionFailed, "Failed to update neatdb.db.", Details: dbPath, Inner: dbResult.Error?.Inner);
        }

        progress?.Report(new OperationProgress("Updating registry counter", null, null));

        try
        {
            _registry.WriteDword(RegistryKeyPath, LastDownloadIdValue, newTaskId);
        }
        catch (Exception ex)
        {
            // DB row already committed at this point -- see class-level doc
            // comment on this known cross-store atomicity limitation.
            return new MdmaError(
                MdmaErrorCode.InjectionFailed,
                "The task was written to neatdb.db, but updating the registry LastDownloadID counter failed. " +
                "The task may not be assigned a fresh ID on next import.",
                Details: newTaskId.ToString(),
                Inner: ex);
        }

        return Result.Ok();
    }

    private static void WriteSegmentsBin(string destPath, List<ChunkRange> orderedChunks)
    {
        using var fs = new FileStream(destPath, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        for (int i = 0; i < orderedChunks.Count; i++)
        {
            var chunk = orderedChunks[i];
            bw.Write((ushort)chunk.Index);
            bw.Write((ushort)i);
            bw.Write(i == orderedChunks.Count - 1 ? -1 : orderedChunks[i + 1].Index);
            bw.Write((ulong)chunk.StartByte);
            bw.Write((ulong)chunk.EndByte);
        }
    }

    private static void InsertIntoDatabase(string originalDbPath, string tempDestPath, int newTaskId, MdmaManifest manifest, TargetAppLocation location)
    {
        File.Copy(originalDbPath, tempDestPath, overwrite: true);

        using var conn = new SqliteConnection($"Data Source={tempDestPath};Pooling=False");
        conn.Open();

        long downloaded = manifest.Chunks.Sum(c => c.DownloadedBytes);
        int pct = manifest.TotalBytes <= 0 ? 0 : (int)(downloaded * 100 / manifest.TotalBytes);
        var status = $"Paused ( {pct}% )";

        var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO downloads (id, url, filename, filesize, status, resumable, folderpath, temppath, mimetype)
            VALUES ($id, $url, $filename, $filesize, $status, 1, $folderpath, $temppath, $mimetype);
            """;
        insert.Parameters.AddWithValue("$id", newTaskId);
        insert.Parameters.AddWithValue("$url", manifest.Url);
        insert.Parameters.AddWithValue("$filename", manifest.Filename);
        insert.Parameters.AddWithValue("$filesize", manifest.TotalBytes);
        insert.Parameters.AddWithValue("$status", status);
        insert.Parameters.AddWithValue("$folderpath", (object?)location.DownloadDirectory ?? DBNull.Value);
        insert.Parameters.AddWithValue("$temppath", location.InstallOrConfigDir!);
        insert.Parameters.AddWithValue("$mimetype", (object?)manifest.MimeType ?? DBNull.Value);
        insert.ExecuteNonQuery();

        foreach (var header in manifest.Headers)
        {
            var headerInsert = conn.CreateCommand();
            headerInsert.CommandText = "INSERT INTO headers (id, header) VALUES ($id, $header);";
            headerInsert.Parameters.AddWithValue("$id", newTaskId);
            headerInsert.Parameters.AddWithValue("$header", $"{header.Key}: {header.Value}");
            headerInsert.ExecuteNonQuery();
        }

        SqliteConnection.ClearPool(conn);
    }

    private static void TryDeleteDirectoryBestEffort(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup of a failed injection attempt */ }
    }
}
