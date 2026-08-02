using System.Security.Cryptography;
using System.Text.Json;

namespace Mdma.Core;

/// <summary>
/// Takes and enumerates versioned backups under <workingRoot>\backups\.
/// Backup scope per the locked-in decisions (mdma-core-plan.md Decisions):
///   NDM: neatdb.db + the specific task's <TempDirectory>\<TaskId>\ folder, if it exists.
///   JD2: the current newest downloadList<N>.zip.
/// This is Critical infrastructure — callers (eventually ConversionService)
/// must not proceed to any destructive write if CreateBackup fails.
/// </summary>
public sealed class BackupManager : IBackupManager
{
    private const string BackupsSubfolder = "backups";
    private readonly IClock _clock;

    public BackupManager(IClock clock)
    {
        _clock = clock;
    }

    public Result<BackupHandle> CreateBackup(TargetAppLocation location, WorkingRoot workingRoot, string? taskNativeId = null)
    {
        var timestamp = _clock.UtcNow;
        // Timestamp + target + short random suffix, to avoid collisions if two
        // backups somehow get requested within the same second.
        var id = $"{timestamp:yyyyMMdd'T'HHmmss'Z'}_{location.App}_{Guid.NewGuid().ToString("N")[..8]}";

        var backupDir = Path.Combine(workingRoot.Path, BackupsSubfolder, id);

        try
        {
            Directory.CreateDirectory(backupDir);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "Could not create the backup snapshot directory.",
                Details: backupDir,
                Inner: ex);
        }

        var entriesResult = location.App switch
        {
            TargetApp.NDM => BackupNdm(location, taskNativeId, backupDir),
            TargetApp.JD2 => BackupJd2(location, backupDir),
            _ => new MdmaError(MdmaErrorCode.BackupFailed, "Unsupported target app.", Details: location.App.ToString()),
        };

        if (!entriesResult.IsSuccess)
        {
            TryDeleteBestEffort(backupDir);
            return entriesResult.Error!;
        }

        var manifest = new BackupManifest(location.App, timestamp, entriesResult.Value!);
        try
        {
            var manifestPath = Path.Combine(backupDir, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        }
        catch (Exception ex)
        {
            TryDeleteBestEffort(backupDir);
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "Could not write the backup manifest.",
                Details: backupDir,
                Inner: ex);
        }

        return Result<BackupHandle>.Ok(new BackupHandle(id, location.App, timestamp, backupDir));
    }

    public Result<IReadOnlyList<BackupHandle>> ListBackups(WorkingRoot workingRoot, TargetApp? filterBy = null)
    {
        var backupsRoot = Path.Combine(workingRoot.Path, BackupsSubfolder);
        if (!Directory.Exists(backupsRoot))
        {
            return Result<IReadOnlyList<BackupHandle>>.Ok(Array.Empty<BackupHandle>());
        }

        var handles = new List<BackupHandle>();
        foreach (var dir in Directory.GetDirectories(backupsRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue; // not a valid snapshot, skip rather than fail the whole listing

            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (manifest is null) continue;
                if (filterBy is not null && manifest.Target != filterBy) continue;

                handles.Add(new BackupHandle(Path.GetFileName(dir), manifest.Target, manifest.CreatedAt, dir));
            }
            catch
            {
                // Corrupt manifest for one snapshot shouldn't hide every other
                // valid snapshot from the list — skip it, don't fail ListBackups.
                continue;
            }
        }

        handles.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt)); // newest first
        return Result<IReadOnlyList<BackupHandle>>.Ok(handles);
    }

    private static Result<IReadOnlyList<BackupManifestEntry>> BackupNdm(TargetAppLocation location, string? taskNativeId, string backupDir)
    {
        if (location.MetadataDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "No metadata directory (neatdb.db location) is known for this NDM location.",
                Details: "TargetAppLocation.MetadataDir was null.");
        }

        var dbPath = Path.Combine(location.MetadataDir, "neatdb.db");
        if (!File.Exists(dbPath))
        {
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "neatdb.db was not found — nothing to back up.",
                Details: dbPath);
        }

        var entries = new List<BackupManifestEntry>();

        var dbEntry = CopyAndHash(dbPath, Path.Combine(backupDir, "neatdb.db"), "neatdb.db");
        if (!dbEntry.IsSuccess) return dbEntry.Error!;
        entries.Add(dbEntry.Value!);

        if (taskNativeId is not null && location.InstallOrConfigDir is not null)
        {
            var taskDir = Path.Combine(location.InstallOrConfigDir, taskNativeId);
            if (Directory.Exists(taskDir))
            {
                var taskBackupRoot = Path.Combine(backupDir, "task", taskNativeId);
                foreach (var file in Directory.GetFiles(taskDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(taskDir, file);
                    var backupRelative = Path.Combine("task", taskNativeId, relative);
                    var entry = CopyAndHash(file, Path.Combine(backupDir, backupRelative), backupRelative);
                    if (!entry.IsSuccess) return entry.Error!;
                    entries.Add(entry.Value!);
                }
            }
            // Task directory not existing is fine (task hasn't started/no segments
            // yet) -- not an error, just nothing extra to back up.
        }

        return Result<IReadOnlyList<BackupManifestEntry>>.Ok(entries);
    }

    private static Result<IReadOnlyList<BackupManifestEntry>> BackupJd2(TargetAppLocation location, string backupDir)
    {
        if (location.InstallOrConfigDir is null)
        {
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "No cfg\\ directory is known for this JD2 location.",
                Details: "TargetAppLocation.InstallOrConfigDir was null.");
        }

        var candidates = Directory.GetFiles(location.InstallOrConfigDir, "downloadList*.zip");
        if (candidates.Length == 0)
        {
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "No downloadList*.zip file was found — nothing to back up.",
                Details: location.InstallOrConfigDir);
        }

        var newestZip = Jd2Locator.PickNewest(candidates);
        var fileName = Path.GetFileName(newestZip);

        var entry = CopyAndHash(newestZip, Path.Combine(backupDir, fileName), fileName);
        if (!entry.IsSuccess) return entry.Error!;

        return Result<IReadOnlyList<BackupManifestEntry>>.Ok(new[] { entry.Value! });
    }

    private static Result<BackupManifestEntry> CopyAndHash(string sourcePath, string destinationPath, string backupRelativePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(destinationPath);
            var hash = Convert.ToHexString(sha.ComputeHash(stream));

            return Result<BackupManifestEntry>.Ok(new BackupManifestEntry(sourcePath, backupRelativePath, hash));
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.BackupFailed,
                "Failed to copy a file into the backup snapshot.",
                Details: sourcePath,
                Inner: ex);
        }
    }

    private static void TryDeleteBestEffort(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of a partially-created, failed backup attempt.
        }
    }
}
