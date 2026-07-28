namespace Mdma.Core;

/// <summary>
/// Marks whether a step's failure should abort the whole operation (Critical)
/// or just be logged and reported without failing the operation (BestEffort).
/// See architecture.md §8 "Step criticality" — this is meant to be a first-class
/// concept callers check explicitly, not ad hoc try/catch.
/// </summary>
public enum StepCriticality
{
    Critical,
    BestEffort,
}

/// <summary>Write-to-temp-then-rename for a single file. Used for every mutation
/// of neatdb.db, downloadList*.zip, or any file MDMA writes — never write in place.</summary>
public interface IAtomicWriter
{
    /// <summary>writeAction receives a path to a temp file to write into; on success
    /// this is renamed over destinationPath, on failure the temp file is discarded
    /// and destinationPath is left untouched.</summary>
    Result WriteAtomic(string destinationPath, Action<string> writeAction);
}

/// <summary>Takes and enumerates versioned backups of a target app's state before
/// any mutation. Backup success is Critical — callers must not proceed to a
/// destructive write if this fails.</summary>
public interface IBackupManager
{
    /// <summary>Snapshots everything MDMA is about to touch for `app` (db file,
    /// relevant registry keys, cfg folder, etc. — target-specific) into the
    /// working root's backups directory.</summary>
    Result<BackupHandle> CreateBackup(TargetApp app, WorkingRoot workingRoot);

    Result<IReadOnlyList<BackupHandle>> ListBackups(WorkingRoot workingRoot, TargetApp? filterBy = null);
}

/// <summary>Restores a specific prior backup, undoing one MDMA operation.</summary>
public interface IRevertManager
{
    Result Revert(BackupHandle backup);
}
