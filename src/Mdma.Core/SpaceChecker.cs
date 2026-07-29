namespace Mdma.Core;

/// <summary>
/// Validates that a path's volume has enough free space for an operation,
/// including a safety margin for zip/checksum overhead. Used on both the
/// MDMA working root (source/staging side) and a target app's directory
/// (destination side) — callers specify which via isDestination so the
/// correct MdmaErrorCode and messaging is used.
/// </summary>
public sealed class SpaceChecker : ISpaceChecker
{
    /// <summary>Extra headroom required on top of the raw byte requirement,
    /// to account for zip overhead, checksum computation, and general safety
    /// margin. 0.15 = 15%, matching the upper end of the 10-15% range in
    /// architecture.md §7.</summary>
    public const double SafetyMarginFraction = 0.15;

    private readonly IDiskSpaceSource _diskSpaceSource;

    public SpaceChecker(IDiskSpaceSource diskSpaceSource)
    {
        _diskSpaceSource = diskSpaceSource;
    }

    public Result HasSufficientSpace(string path, long requiredBytes, bool isDestination)
    {
        if (requiredBytes < 0)
        {
            return new MdmaError(
                MdmaErrorCode.Unknown,
                "Required byte count cannot be negative.",
                Details: $"requiredBytes={requiredBytes}");
        }

        long requiredWithMargin = (long)Math.Ceiling(requiredBytes * (1.0 + SafetyMarginFraction));
        long available = _diskSpaceSource.GetAvailableFreeBytes(path);

        if (available >= requiredWithMargin)
        {
            return Result.Ok();
        }

        long shortfall = requiredWithMargin - available;
        var code = isDestination
            ? MdmaErrorCode.InsufficientDiskSpaceDestination
            : MdmaErrorCode.InsufficientDiskSpaceSource;

        return new MdmaError(
            code,
            "Not enough free disk space for this operation.",
            Details: $"Required {FormatBytes(requiredWithMargin)} (incl. {SafetyMarginFraction:P0} margin), " +
                     $"available {FormatBytes(available)} at '{path}'. Short by {FormatBytes(shortfall)}.",
            SuggestedAction: isDestination
                ? "Free up space on the destination drive, or choose a different destination directory."
                : "Free up space in the working directory, or point --workdir at a drive with more room.");
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024;
        return bytes switch
        {
            < 0 => $"{bytes} B",
            _ when bytes >= gb => $"{bytes / gb:F2} GB",
            _ when bytes >= mb => $"{bytes / mb:F2} MB",
            _ when bytes >= kb => $"{bytes / kb:F2} KB",
            _ => $"{bytes} B",
        };
    }
}
