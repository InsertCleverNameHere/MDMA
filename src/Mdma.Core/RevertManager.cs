using System.Security.Cryptography;
using System.Text.Json;

namespace Mdma.Core;

/// <summary>
/// Restores a specific backup snapshot, undoing one MDMA operation. Two
/// preconditions are checked before any live file is touched, both fail-fast:
///   1. The target app's process must not be running (IProcessGuard).
///   2. Every file in the snapshot must still match its recorded SHA-256
///      (a tampered/corrupted backup is refused outright, not partially used).
/// Restoration itself goes through IAtomicWriter per file, so a failure
/// partway through a multi-file restore doesn't leave any single destination
/// file half-written.
/// </summary>
public sealed class RevertManager : IRevertManager
{
    private readonly IProcessGuard _processGuard;
    private readonly IAtomicWriter _atomicWriter;
    private readonly IMdmaLogger _logger;

    public RevertManager(
        IProcessGuard processGuard,
        IAtomicWriter atomicWriter,
        IMdmaLogger? logger = null
    )
    {
        _processGuard = processGuard;
        _atomicWriter = atomicWriter;
        _logger = logger ?? NullMdmaLogger.Instance;
    }

    public Result Revert(BackupHandle backup)
    {
        _logger.LogInfo("RevertManager", $"Starting revert operation for backup '{backup.Id}'.");

        var manifestPath = Path.Combine(backup.StoragePath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new MdmaError(
                MdmaErrorCode.RevertTargetNotFound,
                "The backup snapshot's manifest could not be found.",
                Details: manifestPath
            );
        }

        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.RevertFailed,
                "The backup snapshot's manifest is corrupt and could not be read.",
                Details: manifestPath,
                Inner: ex
            );
        }

        if (manifest is null)
        {
            return new MdmaError(
                MdmaErrorCode.RevertFailed,
                "The backup snapshot's manifest deserialized to nothing.",
                Details: manifestPath
            );
        }

        var processCheck = _processGuard.IsSafeToProceed(manifest.Target);
        if (!processCheck.IsSuccess)
        {
            return processCheck.Error!;
        }
        if (!processCheck.Value)
        {
            _logger.LogError(
                "RevertManager",
                $"Revert blocked: target application process is running for {manifest.Target}."
            );
            return new MdmaError(
                MdmaErrorCode.TargetAppProcessRunning,
                "Cannot revert while the target application is running.",
                Details: manifest.Target.ToString(),
                SuggestedAction: "Close the application and try again."
            );
        }

        // Verify every entry's integrity BEFORE restoring anything -- a
        // tampered/corrupted snapshot must be refused wholesale, never
        // partially applied.
        foreach (var entry in manifest.Entries)
        {
            var snapshotFilePath = Path.Combine(backup.StoragePath, entry.BackupRelativePath);
            var integrityCheck = VerifyIntegrity(snapshotFilePath, entry.Sha256);
            if (!integrityCheck.IsSuccess)
            {
                return new MdmaError(
                    MdmaErrorCode.RevertFailed,
                    "A file in the backup snapshot failed integrity verification. Nothing was restored.",
                    Details: entry.OriginalPath,
                    Inner: integrityCheck.Error?.Inner
                );
            }
        }

        // All entries verified -- now actually restore.
        foreach (var entry in manifest.Entries)
        {
            var snapshotFilePath = Path.Combine(backup.StoragePath, entry.BackupRelativePath);
            var bytes = File.ReadAllBytes(snapshotFilePath);

            var writeResult = _atomicWriter.WriteAtomic(
                entry.OriginalPath,
                dest => File.WriteAllBytes(dest, bytes)
            );
            if (!writeResult.IsSuccess)
            {
                return new MdmaError(
                    MdmaErrorCode.RevertFailed,
                    "Failed to restore a file during revert. The operation stopped partway -- "
                        + "some files may already have been restored. Check the other backup entries manually if needed.",
                    Details: entry.OriginalPath,
                    Inner: writeResult.Error?.Inner
                );
            }
        }

        _logger.LogInfo(
            "RevertManager",
            $"Revert operation completed successfully for backup '{backup.Id}'."
        );
        return Result.Ok();
    }

    private static Result VerifyIntegrity(string snapshotFilePath, string expectedSha256)
    {
        if (!File.Exists(snapshotFilePath))
        {
            return new MdmaError(
                MdmaErrorCode.RevertFailed,
                "A file recorded in the backup manifest is missing from the snapshot.",
                Details: snapshotFilePath
            );
        }

        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(snapshotFilePath);
            var actualHash = Convert.ToHexString(sha.ComputeHash(stream));

            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new MdmaError(
                    MdmaErrorCode.RevertFailed,
                    "Checksum mismatch on a backed-up file.",
                    Details: snapshotFilePath
                );
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.RevertFailed,
                "Could not compute checksum for a backed-up file.",
                Details: snapshotFilePath,
                Inner: ex
            );
        }
    }
}
