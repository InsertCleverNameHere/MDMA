namespace Mdma.Core;

/// <summary>
/// Write-to-temp-then-rename for a single file. The temp file is created in the
/// SAME directory as the destination — this is deliberate, not incidental: an
/// OS-level rename is only atomic when source and destination are on the same
/// volume. Writing the temp file elsewhere (e.g. a generic temp folder that
/// might be on a different drive) would silently turn "atomic rename" into a
/// non-atomic copy+delete, defeating the entire point of this class.
/// </summary>
public sealed class AtomicWriter : IAtomicWriter
{
    public Result WriteAtomic(string destinationPath, Action<string> writeAction)
    {
        var destinationDir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrEmpty(destinationDir))
        {
            return new MdmaError(
                MdmaErrorCode.AtomicWriteFailed,
                "Could not determine a parent directory for the destination path.",
                Details: destinationPath);
        }

        try
        {
            Directory.CreateDirectory(destinationDir);
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.AtomicWriteFailed,
                "Could not create the destination directory.",
                Details: destinationDir,
                Inner: ex);
        }

        var tempPath = Path.Combine(destinationDir, $".{Path.GetFileName(destinationPath)}.mdma-tmp-{Guid.NewGuid():N}");

        try
        {
            writeAction(tempPath);
        }
        catch (Exception ex)
        {
            TryDeleteBestEffort(tempPath);
            return new MdmaError(
                MdmaErrorCode.AtomicWriteFailed,
                "The write action failed before the file could be finalized. " +
                "The original destination file, if any, was left untouched.",
                Details: destinationPath,
                Inner: ex);
        }

        if (!File.Exists(tempPath))
        {
            return new MdmaError(
                MdmaErrorCode.AtomicWriteFailed,
                "The write action completed without error but produced no output file.",
                Details: tempPath);
        }

        try
        {
            // File.Move with overwrite:true performs an atomic replace on the
            // same volume on both Windows and Unix in modern .NET. This only
            // stays atomic because tempPath and destinationPath share a directory.
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDeleteBestEffort(tempPath);
            return new MdmaError(
                MdmaErrorCode.AtomicWriteFailed,
                "Could not finalize the write by renaming the temp file over the destination. " +
                "The original destination file, if any, was left untouched.",
                Details: destinationPath,
                Inner: ex);
        }

        return Result.Ok();
    }

    private static void TryDeleteBestEffort(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of our own temp file — if this fails (locked by
            // AV scan, etc.) it's an orphaned .mdma-tmp-* file, not a correctness
            // issue. Startup sweeps (Phase 5/ITempCleanupService) are the backstop.
        }
    }
}
