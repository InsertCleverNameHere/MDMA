using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class ImportViewModel : ObservableObject
{
    private readonly IConversionService _conversionService;
    private readonly ILocationResolver _locationResolver;
    private string _packageFilePath = "";
    private TargetApp _targetApp = TargetApp.NDM;
    private double? _progressPercent;
    private string? _progressStage;
    private string? _progressDetail;
    private bool _isImporting;
    private bool _hasResult;
    private bool _isSuccess;
    private string? _resultMessage;
    private string? _suggestedAction;

    public string PackageFilePath
    {
        get => _packageFilePath;
        set
        {
            if (SetProperty(ref _packageFilePath, value))
            {
                ImportCommand.RaiseCanExecuteChanged();
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

    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetProperty(ref _isImporting, value))
            {
                ImportCommand.RaiseCanExecuteChanged();
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

    public RelayCommand ImportCommand { get; }
    public RelayCommand BrowsePackageCommand { get; }

    public ImportViewModel(IConversionService conversionService, ILocationResolver locationResolver)
    {
        _conversionService = conversionService;
        _locationResolver = locationResolver;

        ImportCommand = new RelayCommand(RunImport, CanImport);
        BrowsePackageCommand = new RelayCommand(() => { });
    }

    public bool CanImport() => !string.IsNullOrWhiteSpace(PackageFilePath) && !IsImporting;

    public void RunImport()
    {
        if (string.IsNullOrWhiteSpace(PackageFilePath))
            return;

        IsImporting = true;
        HasResult = false;
        ProgressPercent = null;
        ProgressStage = "Initializing import...";
        ProgressDetail = null;

        var locationResult = _locationResolver.ResolveLocation(TargetApp);
        if (!locationResult.IsSuccess)
        {
            IsImporting = false;
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

        var importResult = _conversionService.ImportFromFile(
            PackageFilePath,
            locationResult.Value!,
            progress
        );

        IsImporting = false;
        HasResult = true;

        if (importResult.IsSuccess)
        {
            IsSuccess = true;
            ResultMessage = $"Successfully imported package into {TargetApp}.";
            SuggestedAction = null;
        }
        else
        {
            IsSuccess = false;
            ResultMessage = importResult.Error!.Message;
            SuggestedAction = importResult.Error.SuggestedAction;
        }
    }
}
