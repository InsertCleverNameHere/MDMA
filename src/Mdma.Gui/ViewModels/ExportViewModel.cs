using System.IO;
using System.Linq;
using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class ExportViewModel : ObservableObject
{
    private readonly IConversionService _conversionService;
    private readonly ILocationResolver _locationResolver;
    private DownloadTaskSummary? _task;
    private string _destinationPath = "";
    private double? _progressPercent;
    private string? _progressStage;
    private string? _progressDetail;
    private bool _isExporting;
    private bool _hasResult;
    private bool _isSuccess;
    private string? _resultMessage;
    private string? _suggestedAction;

    public DownloadTaskSummary? Task
    {
        get => _task;
        set
        {
            if (SetProperty(ref _task, value))
            {
                if (_task is not null)
                {
                    DestinationPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"{SanitizeFileName(_task.Filename)}.mdma"
                    );
                }
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double? ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string? ProgressStage
    {
        get => _progressStage;
        set => SetProperty(ref _progressStage, value);
    }

    public string? ProgressDetail
    {
        get => _progressDetail;
        set => SetProperty(ref _progressDetail, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        set
        {
            if (SetProperty(ref _isExporting, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasResult
    {
        get => _hasResult;
        set => SetProperty(ref _hasResult, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        set => SetProperty(ref _isSuccess, value);
    }

    public string? ResultMessage
    {
        get => _resultMessage;
        set => SetProperty(ref _resultMessage, value);
    }

    public string? SuggestedAction
    {
        get => _suggestedAction;
        set => SetProperty(ref _suggestedAction, value);
    }

    public RelayCommand ExportCommand { get; }
    public RelayCommand BrowseDestinationCommand { get; }

    public ExportViewModel(IConversionService conversionService, ILocationResolver locationResolver)
    {
        _conversionService = conversionService;
        _locationResolver = locationResolver;

        ExportCommand = new RelayCommand(RunExport, CanExport);
        BrowseDestinationCommand = new RelayCommand(() => { });
    }

    public bool CanExport() =>
        Task is not null && !string.IsNullOrWhiteSpace(DestinationPath) && !IsExporting;

    public void RunExport()
    {
        if (Task is null || string.IsNullOrWhiteSpace(DestinationPath))
            return;

        IsExporting = true;
        HasResult = false;
        ProgressPercent = null;
        ProgressStage = "Initializing export...";
        ProgressDetail = null;

        var locationResult = _locationResolver.ResolveLocation(Task.Source);
        if (!locationResult.IsSuccess)
        {
            IsExporting = false;
            HasResult = true;
            IsSuccess = false;
            ResultMessage = locationResult.Error!.Message;
            SuggestedAction = locationResult.Error.SuggestedAction;
            return;
        }

        var progress = new Progress<OperationProgress>(p =>
        {
            ProgressStage = p.Stage;
            ProgressPercent = p.PercentComplete;
            ProgressDetail = p.Detail;
        });

        var exportResult = _conversionService.ExportToFile(
            Task,
            locationResult.Value!,
            DestinationPath,
            progress
        );

        IsExporting = false;
        HasResult = true;

        if (exportResult.IsSuccess)
        {
            IsSuccess = true;
            ResultMessage = $"Successfully exported task to '{exportResult.Value!}'.";
            SuggestedAction = null;
        }
        else
        {
            IsSuccess = false;
            ResultMessage = exportResult.Error!.Message;
            SuggestedAction = exportResult.Error.SuggestedAction;
        }
    }

    public static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
