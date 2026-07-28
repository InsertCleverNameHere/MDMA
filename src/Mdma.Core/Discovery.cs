namespace Mdma.Core;

/// <summary>Where a target app's relevant state lives, once found/confirmed.</summary>
public sealed record TargetAppLocation(
    TargetApp App,
    string InstallOrConfigDir,   // e.g. NDM's TempDirectory, or JD2's cfg\ folder
    string DownloadDirectory,    // default output dir, needed for space checks on import
    bool WasAutoDetected);

/// <summary>
/// Finds a target app's install/config location. Implemented once per target
/// (NDM, JD2, ...). Must never throw for "not found" — that's an expected,
/// common outcome, not an exceptional one.
/// </summary>
public interface IDownloadManagerLocator
{
    TargetApp App { get; }

    /// <summary>Attempts registry/config-based auto-detection. Returns a typed
    /// failure (TargetAppNotFound), never throws, if nothing is found.</summary>
    Result<TargetAppLocation> TryAutoDetect();

    /// <summary>Validates a user-supplied path actually contains what this target
    /// expects (e.g. neatdb.db opens cleanly, or cfg\ has a downloadList*.zip).
    /// Used when auto-detect fails and the user points MDMA at a folder manually.</summary>
    Result<TargetAppLocation> ValidateManualPath(string path);
}

/// <summary>
/// Reads a target app's current task list into the normalized DownloadTaskSummary
/// shape. One implementation per target.
/// </summary>
public interface IDownloadListReader
{
    TargetApp App { get; }

    /// <summary>Lists all tasks currently known to the target app at the given location.
    /// Read-only — must not require a process guard or backup.</summary>
    Result<IReadOnlyList<DownloadTaskSummary>> ScanTasks(TargetAppLocation location);
}

/// <summary>Confirms a target app's process is not currently running, so its files
/// are safe to read/write without lock conflicts or shutdown-hook overwrites.</summary>
public interface IProcessGuard
{
    /// <summary>True if the target app process is NOT running (i.e. safe to proceed).</summary>
    Result<bool> IsSafeToProceed(TargetApp app);
}
