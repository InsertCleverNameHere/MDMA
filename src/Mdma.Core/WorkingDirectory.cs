namespace Mdma.Core;

/// <summary>
/// Resolved, validated working root for this run — where temp .mdma files, backups,
/// and logs are staged. Resolved once at startup per the precedence order in
/// architecture.md §7: explicit override -> portable default next to exe -> AppData fallback.
/// </summary>
public sealed record WorkingRoot(
    string Path,
    bool IsPortableDefault,   // true if this is <exe dir>\MDMA_Work
    bool IsFallback);         // true if this had to fall back to %LOCALAPPDATA%

public interface IWorkingDirectoryProvider
{
    /// <summary>Resolves and validates the working root once. explicitOverride comes
    /// from --workdir (CLI) or the persisted GUI setting; pass null to use the
    /// portable-default / fallback chain.</summary>
    Result<WorkingRoot> Resolve(string? explicitOverride);
}

/// <summary>Disk space precondition checks. Applied to both the MDMA working root
/// (export/staging side) and the destination target app's directory (import side) —
/// see architecture.md §7, both ends must be validated before a conversion starts.</summary>
public interface ISpaceChecker
{
    /// <summary>Checks `path`'s volume has at least requiredBytes free, plus the
    /// standard safety margin. Returns InsufficientDiskSpaceSource or
    /// InsufficientDiskSpaceDestination (caller specifies which via isDestination)
    /// with the exact shortfall in MdmaError.Details.</summary>
    Result HasSufficientSpace(string path, long requiredBytes, bool isDestination);
}
