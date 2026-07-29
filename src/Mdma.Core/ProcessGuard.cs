namespace Mdma.Core;

/// <summary>
/// Confirms a target app's process is not running before MDMA touches its files.
/// Process name mapping per target — see docs/ndm.md §6 Step 1 and docs/jd2.md
/// §5 Step 1. JD2 has two possible executable names depending on install/launch
/// method; NDM has one.
/// </summary>
public sealed class ProcessGuard : IProcessGuard
{
    private static readonly IReadOnlyDictionary<TargetApp, string[]> ProcessNamesByTarget =
        new Dictionary<TargetApp, string[]>
        {
            [TargetApp.NDM] = new[] { "NeatDownloadManager.exe" },
            [TargetApp.JD2] = new[] { "JDownloader2.exe", "JDownloader.exe" },
        };

    private readonly IProcessLister _processLister;

    public ProcessGuard(IProcessLister processLister)
    {
        _processLister = processLister;
    }

    public Result<bool> IsSafeToProceed(TargetApp app)
    {
        if (!ProcessNamesByTarget.TryGetValue(app, out var processNames))
        {
            return new MdmaError(
                MdmaErrorCode.Unknown,
                "No process name mapping is defined for this target app.",
                Details: app.ToString());
        }

        foreach (var name in processNames)
        {
            if (_processLister.IsRunning(name))
            {
                return Result<bool>.Ok(false);
            }
        }

        return Result<bool>.Ok(true);
    }
}
