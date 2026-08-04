namespace Mdma.Core;

/// <summary>Report of what a sweep found. Removed = successfully deleted.
/// FailedToRemove = found but couldn't be deleted (locked, in use, etc.) --
/// the sweep is best-effort by nature, so these are reported, not thrown.</summary>
public sealed record TempCleanupReport(
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> FailedToRemove
);

/// <summary>
/// Sweeps <workingRoot>\.mdma-tmp\ for orphaned files/folders left behind by
/// a crashed or interrupted prior run (a temp .mdma from an interrupted
/// ConvertSameMachine, a staged extraction folder from MdmaLoader, a staged
/// slicing folder from Jd2Exporter, etc.). Since MDMA is single-operation-at-
/// a-time and every code path that creates something under .mdma-tmp is
/// responsible for cleaning up after itself on success, ANYTHING still
/// present there when this runs (typically at startup, or on user request)
/// is by definition orphaned -- no further identification logic is needed.
/// </summary>
public interface ITempCleanupService
{
    Result<TempCleanupReport> SweepOrphans(WorkingRoot workingRoot);
}

public sealed class TempCleanupService : ITempCleanupService
{
    private const string TempSubfolder = ".mdma-tmp";
    private readonly IMdmaLogger _logger;

    public TempCleanupService(IMdmaLogger? logger = null)
    {
        _logger = logger ?? NullMdmaLogger.Instance;
    }

    public Result<TempCleanupReport> SweepOrphans(WorkingRoot workingRoot)
    {
        _logger.LogDebug("TempCleanupService", "Starting orphan sweep in .mdma-tmp...");

        var tempDir = Path.Combine(workingRoot.Path, TempSubfolder);
        if (!Directory.Exists(tempDir))
        {
            _logger.LogDebug(
                "TempCleanupService",
                ".mdma-tmp directory does not exist; nothing to sweep."
            );
            return Result<TempCleanupReport>.Ok(
                new TempCleanupReport(Array.Empty<string>(), Array.Empty<string>())
            );
        }

        var removed = new List<string>();
        var failed = new List<string>();

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(tempDir);
        }
        catch (Exception)
        {
            // Can't even list the directory -- report nothing removable rather
            // than failing the whole sweep; a locked/inaccessible .mdma-tmp
            // itself is an edge case worth surfacing as "0 removed", not a crash.
            return Result<TempCleanupReport>.Ok(
                new TempCleanupReport(Array.Empty<string>(), Array.Empty<string>())
            );
        }

        foreach (var entry in entries)
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else if (File.Exists(entry))
                {
                    File.Delete(entry);
                }
                removed.Add(entry);
            }
            catch
            {
                failed.Add(entry);
            }
        }

        if (removed.Count > 0)
        {
            _logger.LogInfo(
                "TempCleanupService",
                $"Sweep complete. Removed {removed.Count} orphaned items.",
                details: string.Join("; ", removed)
            );
        }
        if (failed.Count > 0)
        {
            _logger.LogWarning(
                "TempCleanupService",
                $"Failed to remove {failed.Count} orphaned items during sweep.",
                details: string.Join("; ", failed)
            );
        }

        return Result<TempCleanupReport>.Ok(new TempCleanupReport(removed, failed));
    }
}
