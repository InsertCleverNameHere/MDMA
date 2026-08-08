using System.Collections.ObjectModel;
using System.Linq;
using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class AppStatusViewModel : ObservableObject
{
    private string _statusText = "";
    private bool _isFound;
    private bool _isScanning;
    private string? _manualPath;

    public TargetApp App { get; }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsFound
    {
        get => _isFound;
        set => SetProperty(ref _isFound, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public string? ManualPath
    {
        get => _manualPath;
        set => SetProperty(ref _manualPath, value);
    }

    public AppStatusViewModel(TargetApp app)
    {
        App = app;
        StatusText = $"{app}: Checking...";
    }
}

public sealed class ScanViewModel : ObservableObject
{
    public Action<DownloadTaskSummary>? OnConvertRequested { get; set; }
    public RelayCommand ConvertSelectedCommand { get; }
    public Action<DownloadTaskSummary>? OnExportRequested { get; set; }
    public RelayCommand ExportSelectedCommand { get; }
    private readonly IDownloadManagerLocator _ndmLocator;
    private readonly IDownloadManagerLocator _jd2Locator;
    private readonly IDownloadListReader _ndmReader;
    private readonly IDownloadListReader _jd2Reader;
    private DownloadTaskSummary? _selectedTask;
    private bool _isScanning;

    public AppStatusViewModel NdmStatus { get; } = new(TargetApp.NDM);
    public AppStatusViewModel Jd2Status { get; } = new(TargetApp.JD2);

    public ObservableCollection<DownloadTaskSummary> Tasks { get; } = new();

    public DownloadTaskSummary? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetProperty(ref _selectedTask, value))
            {
                ExportSelectedCommand.RaiseCanExecuteChanged();
                ConvertSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand<TargetApp> BrowseManualPathCommand { get; }

    public ScanViewModel(
        IDownloadManagerLocator ndmLocator,
        IDownloadManagerLocator jd2Locator,
        IDownloadListReader ndmReader,
        IDownloadListReader jd2Reader
    )
    {
        _ndmLocator = ndmLocator;
        _jd2Locator = jd2Locator;
        _ndmReader = ndmReader;
        _jd2Reader = jd2Reader;

        RefreshCommand = new RelayCommand(RunScan, () => !IsScanning);
        BrowseManualPathCommand = new RelayCommand<TargetApp>(_ => { }, _ => !IsScanning);
        ExportSelectedCommand = new RelayCommand(
            () =>
            {
                if (SelectedTask is not null)
                    OnExportRequested?.Invoke(SelectedTask);
            },
            () => SelectedTask is not null && !IsScanning
        );
        ConvertSelectedCommand = new RelayCommand(
            () =>
            {
                if (SelectedTask is not null)
                    OnConvertRequested?.Invoke(SelectedTask);
            },
            () => SelectedTask is not null && !IsScanning
        );
    }

    public void RunScan()
    {
        IsScanning = true;
        Tasks.Clear();

        ScanApp(TargetApp.NDM, _ndmLocator, _ndmReader, NdmStatus);
        ScanApp(TargetApp.JD2, _jd2Locator, _jd2Reader, Jd2Status);

        IsScanning = false;
    }

    public void SetManualPath(TargetApp app, string path)
    {
        var status = app == TargetApp.NDM ? NdmStatus : Jd2Status;
        status.ManualPath = path;
        var locator = app == TargetApp.NDM ? _ndmLocator : _jd2Locator;
        var reader = app == TargetApp.NDM ? _ndmReader : _jd2Reader;

        var oldTasks = Tasks.Where(t => t.Source == app).ToList();
        foreach (var old in oldTasks)
            Tasks.Remove(old);

        ScanApp(app, locator, reader, status);
    }

    private void ScanApp(
        TargetApp app,
        IDownloadManagerLocator locator,
        IDownloadListReader reader,
        AppStatusViewModel status
    )
    {
        status.IsScanning = true;

        Result<TargetAppLocation> locationResult = !string.IsNullOrEmpty(status.ManualPath)
            ? locator.ValidateManualPath(status.ManualPath)
            : locator.TryAutoDetect();

        if (!locationResult.IsSuccess)
        {
            status.IsFound = false;
            status.StatusText = $"{app}: Not found ({locationResult.Error!.Message})";
            status.IsScanning = false;
            return;
        }

        var location = locationResult.Value!;
        var scanResult = reader.ScanTasks(location);

        if (!scanResult.IsSuccess)
        {
            status.IsFound = false;
            status.StatusText = $"{app}: Scan failed ({scanResult.Error!.Message})";
            status.IsScanning = false;
            return;
        }

        status.IsFound = true;
        var appTasks = scanResult.Value!;
        status.StatusText =
            $"{app}: Connected ({appTasks.Count} task{(appTasks.Count == 1 ? "" : "s")})";

        foreach (var task in appTasks)
        {
            Tasks.Add(task);
        }

        status.IsScanning = false;
    }
}
