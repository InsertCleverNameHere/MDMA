using System.Diagnostics;
using System.IO;
using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ITempCleanupService _cleanupService;
    private readonly IGuiSettingsStore _settingsStore;
    private readonly WorkingRoot _workingRoot;

    private bool _hasSweepResult;
    private int _removedCount;
    private int _failedCount;
    private string? _sweepSummary;

    public string WorkingRootPath => _workingRoot.Path;
    public bool IsPortableDefault => _workingRoot.IsPortableDefault;
    public bool IsFallback => _workingRoot.IsFallback;

    public string StorageTypeDescription =>
        IsPortableDefault ? "Portable (Stored next to application executable)"
        : IsFallback ? "Fallback (Stored in %LOCALAPPDATA% due to permission restrictions)"
        : "Custom Override Path";

    public bool HasSweepResult
    {
        get => _hasSweepResult;
        set => SetProperty(ref _hasSweepResult, value);
    }

    public int RemovedCount
    {
        get => _removedCount;
        set => SetProperty(ref _removedCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        set => SetProperty(ref _failedCount, value);
    }

    public string? SweepSummary
    {
        get => _sweepSummary;
        set => SetProperty(ref _sweepSummary, value);
    }

    public RelayCommand SweepOrphansCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }

    public SettingsViewModel(
        ITempCleanupService cleanupService,
        IGuiSettingsStore settingsStore,
        WorkingRoot workingRoot
    )
    {
        _cleanupService = cleanupService;
        _settingsStore = settingsStore;
        _workingRoot = workingRoot;

        SweepOrphansCommand = new RelayCommand(RunSweepOrphans);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
    }

    public void RunSweepOrphans()
    {
        var result = _cleanupService.SweepOrphans(_workingRoot);
        HasSweepResult = true;

        if (result.IsSuccess)
        {
            RemovedCount = result.Value!.Removed.Count;
            FailedCount = result.Value.FailedToRemove.Count;

            if (RemovedCount == 0 && FailedCount == 0)
            {
                SweepSummary = "No orphaned temporary files were found in .mdma-tmp.";
            }
            else
            {
                SweepSummary =
                    $"Sweep finished: {RemovedCount} item(s) removed, {FailedCount} item(s) locked.";
            }
        }
        else
        {
            RemovedCount = 0;
            FailedCount = 0;
            SweepSummary = $"Sweep failed: {result.Error!.Message}";
        }
    }

    public void OpenLogsFolder()
    {
        var logsDir = Path.Combine(_workingRoot.Path, "logs");
        try
        {
            Directory.CreateDirectory(logsDir);
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = logsDir,
                    UseShellExecute = true,
                    Verb = "open",
                }
            );
        }
        catch
        {
            // Best-effort folder open
        }
    }
}
