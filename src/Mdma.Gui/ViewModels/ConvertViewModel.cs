using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class ConvertViewModel : ObservableObject
{
    private readonly IConversionService _conversionService;
    private readonly ILocationResolver _locationResolver;
    private DownloadTaskSummary? _task;
    private TargetApp _targetApp = TargetApp.JD2;
    private double? _progressPercent;
    private string? _progressStage;
    private string? _progressDetail;
    private bool _isConverting;
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
                    // Default target app to the opposite of the task's source app
                    TargetApp = _task.Source == TargetApp.NDM ? TargetApp.JD2 : TargetApp.NDM;
                }
                ConvertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TargetApp TargetApp
    {
        get => _targetApp;
        set => SetProperty(ref _targetApp, value);
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

    public bool IsConverting
    {
        get => _isConverting;
        set
        {
            if (SetProperty(ref _isConverting, value))
            {
                ConvertCommand.RaiseCanExecuteChanged();
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

    public RelayCommand ConvertCommand { get; }

    public ConvertViewModel(
        IConversionService conversionService,
        ILocationResolver locationResolver
    )
    {
        _conversionService = conversionService;
        _locationResolver = locationResolver;

        ConvertCommand = new RelayCommand(RunConvert, CanConvert);
    }

    public bool CanConvert() => Task is not null && !IsConverting;

    public void RunConvert()
    {
        if (Task is null)
            return;

        IsConverting = true;
        HasResult = false;
        ProgressPercent = null;
        ProgressStage = "Initializing conversion...";
        ProgressDetail = null;

        var sourceLocationResult = _locationResolver.ResolveLocation(Task.Source);
        if (!sourceLocationResult.IsSuccess)
        {
            IsConverting = false;
            HasResult = true;
            IsSuccess = false;
            ResultMessage = sourceLocationResult.Error!.Message;
            SuggestedAction = sourceLocationResult.Error.SuggestedAction;
            return;
        }

        var destLocationResult = _locationResolver.ResolveLocation(TargetApp);
        if (!destLocationResult.IsSuccess)
        {
            IsConverting = false;
            HasResult = true;
            IsSuccess = false;
            ResultMessage = destLocationResult.Error!.Message;
            SuggestedAction = destLocationResult.Error.SuggestedAction;
            return;
        }

        var progress = new Progress<OperationProgress>(p =>
        {
            ProgressStage = p.Stage;
            ProgressPercent = p.PercentComplete;
            ProgressDetail = p.Detail;
        });

        var convertResult = _conversionService.ConvertSameMachine(
            Task,
            sourceLocationResult.Value!,
            destLocationResult.Value!,
            progress
        );

        IsConverting = false;
        HasResult = true;

        if (convertResult.IsSuccess)
        {
            IsSuccess = true;
            ResultMessage =
                $"Successfully converted task '{Task.Filename}' from {Task.Source} to {TargetApp}.";
            SuggestedAction = null;
        }
        else
        {
            IsSuccess = false;
            ResultMessage = convertResult.Error!.Message;
            SuggestedAction = convertResult.Error.SuggestedAction;
        }
    }
}
