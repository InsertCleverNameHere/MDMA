namespace Mdma.Core;

/// <summary>
/// Resolves and validates the working root per the precedence in architecture.md §7:
/// explicit override -> portable default next to the exe -> AppData fallback.
///
/// baseDirectory and localAppDataDirectory are constructor-injectable so tests
/// never touch the real exe location or the real %LOCALAPPDATA% — they default
/// to the real values (AppContext.BaseDirectory / Environment.SpecialFolder.LocalApplicationData)
/// when not supplied, which is what production code gets for free.
/// </summary>
public sealed class WorkingDirectoryProvider : IWorkingDirectoryProvider
{
    private const string PortableFolderName = "MDMA_Work";
    private const string FallbackSubfolder1 = "MDMA";
    private const string FallbackSubfolder2 = "work";

    private readonly string _baseDirectory;
    private readonly string _localAppDataDirectory;

    public WorkingDirectoryProvider(string? baseDirectory = null, string? localAppDataDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _localAppDataDirectory = localAppDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public Result<WorkingRoot> Resolve(string? explicitOverride)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            var ensured = EnsureDirectoryExists(explicitOverride);
            if (!ensured.IsSuccess) return ensured.Error!;

            var probe = ProbeWritable(explicitOverride);
            if (!probe.IsSuccess)
            {
                return new MdmaError(
                    MdmaErrorCode.WorkingDirectoryUnwritable,
                    "The specified working directory is not writable.",
                    Details: explicitOverride,
                    SuggestedAction: "Choose a different --workdir, or fix permissions on this folder.");
            }

            return Result<WorkingRoot>.Ok(new WorkingRoot(explicitOverride, IsPortableDefault: false, IsFallback: false));
        }

        var portablePath = Path.Combine(_baseDirectory, PortableFolderName);
        if (EnsureDirectoryExists(portablePath).IsSuccess && ProbeWritable(portablePath).IsSuccess)
        {
            return Result<WorkingRoot>.Ok(new WorkingRoot(portablePath, IsPortableDefault: true, IsFallback: false));
        }

        var fallbackPath = Path.Combine(_localAppDataDirectory, FallbackSubfolder1, FallbackSubfolder2);
        if (EnsureDirectoryExists(fallbackPath).IsSuccess && ProbeWritable(fallbackPath).IsSuccess)
        {
            // IsFallback = true is the signal callers (Cli/Gui) check to surface a
            // visible warning to the user, per architecture.md §7 — resolution still
            // succeeds, it just wasn't able to honor the portable-by-default goal.
            return Result<WorkingRoot>.Ok(new WorkingRoot(fallbackPath, IsPortableDefault: false, IsFallback: true));
        }

        return new MdmaError(
            MdmaErrorCode.WorkingDirectoryUnwritable,
            "Could not find any writable location for the MDMA working directory.",
            Details: $"Tried portable default '{portablePath}' and fallback '{fallbackPath}'.",
            SuggestedAction: "Specify --workdir explicitly to point at a writable location.");
    }

    private static Result EnsureDirectoryExists(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.WorkingDirectoryUnwritable,
                "Could not create the working directory.",
                Details: path,
                Inner: ex);
        }
    }

    /// <summary>Real write+delete probe — deliberately not a permissions-bit check,
    /// since junctions/network drives/AV locks can misreport those.</summary>
    private static Result ProbeWritable(string path)
    {
        var probeFile = Path.Combine(path, $".mdma-write-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probeFile, new byte[] { 0 });
            File.Delete(probeFile);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new MdmaError(
                MdmaErrorCode.WorkingDirectoryUnwritable,
                "Write probe failed for this directory.",
                Details: path,
                Inner: ex);
        }
    }
}
