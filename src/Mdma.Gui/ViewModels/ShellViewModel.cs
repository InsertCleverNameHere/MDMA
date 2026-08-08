using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private bool _isBusy;
    private bool _isFallbackWorkingRoot;
    private bool _hasBlockingError;
    private string? _errorMessage;
    private string? _suggestedAction;
    private string _workingRootPath = "";

    public INavigationService NavigationService => _navigationService;
    public ScanViewModel ScanViewModel { get; }
    public ExportViewModel ExportViewModel { get; }
    public ImportViewModel ImportViewModel { get; }
    public ConvertViewModel ConvertViewModel { get; }
    public BackupsViewModel BackupsViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NavigateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsFallbackWorkingRoot
    {
        get => _isFallbackWorkingRoot;
        set => SetProperty(ref _isFallbackWorkingRoot, value);
    }

    public bool HasBlockingError
    {
        get => _hasBlockingError;
        set
        {
            if (SetProperty(ref _hasBlockingError, value))
            {
                NavigateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string? SuggestedAction
    {
        get => _suggestedAction;
        set => SetProperty(ref _suggestedAction, value);
    }

    public string WorkingRootPath
    {
        get => _workingRootPath;
        set => SetProperty(ref _workingRootPath, value);
    }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand DismissWarningCommand { get; }

    public ShellViewModel(
        INavigationService navigationService,
        ScanViewModel scanViewModel,
        ExportViewModel exportViewModel,
        ImportViewModel importViewModel,
        ConvertViewModel convertViewModel,
        BackupsViewModel backupsViewModel,
        SettingsViewModel settingsViewModel
    )
    {
        _navigationService = navigationService;
        ScanViewModel = scanViewModel;
        ExportViewModel = exportViewModel;
        ImportViewModel = importViewModel;
        ConvertViewModel = convertViewModel;
        BackupsViewModel = backupsViewModel;
        SettingsViewModel = settingsViewModel;

        ScanViewModel.OnExportRequested = task =>
        {
            ExportViewModel.Task = task;
            _navigationService.NavigateTo(ViewScreen.Export, ExportViewModel);
        };

        ScanViewModel.OnConvertRequested = task =>
        {
            ConvertViewModel.Task = task;
            _navigationService.NavigateTo(ViewScreen.Convert, ConvertViewModel);
        };

        _navigationService.NavigateTo(ViewScreen.Scan, ScanViewModel);

        NavigateCommand = new RelayCommand(
            param =>
            {
                if (
                    param is ViewScreen screen
                    || (param is string s && Enum.TryParse<ViewScreen>(s, true, out screen))
                )
                {
                    switch (screen)
                    {
                        case ViewScreen.Scan:
                            _navigationService.NavigateTo(ViewScreen.Scan, ScanViewModel);
                            break;
                        case ViewScreen.Export:
                            _navigationService.NavigateTo(ViewScreen.Export, ExportViewModel);
                            break;
                        case ViewScreen.Import:
                            _navigationService.NavigateTo(ViewScreen.Import, ImportViewModel);
                            break;
                        case ViewScreen.Convert:
                            _navigationService.NavigateTo(ViewScreen.Convert, ConvertViewModel);
                            break;
                        case ViewScreen.Backups:
                            BackupsViewModel.RefreshBackups();
                            _navigationService.NavigateTo(ViewScreen.Backups, BackupsViewModel);
                            break;
                        case ViewScreen.Settings:
                            _navigationService.NavigateTo(ViewScreen.Settings, SettingsViewModel);
                            break;
                        default:
                            _navigationService.NavigateTo(
                                screen,
                                new DummyViewModel(screen.ToString())
                            );
                            break;
                    }
                }
            },
            _ => !IsBusy && !HasBlockingError
        );

        DismissWarningCommand = new RelayCommand(() => IsFallbackWorkingRoot = false);
    }

    public void InitializeWorkingRoot(
        IWorkingDirectoryProvider provider,
        string? explicitOverride = null
    )
    {
        var result = provider.Resolve(explicitOverride);
        if (result.IsSuccess)
        {
            WorkingRootPath = result.Value!.Path;
            IsFallbackWorkingRoot = result.Value.IsFallback;
            HasBlockingError = false;
        }
        else
        {
            HasBlockingError = true;
            ErrorMessage = result.Error!.Message;
            SuggestedAction = result.Error.SuggestedAction;
        }
    }
}

public sealed class DummyViewModel : ObservableObject
{
    public string Title { get; }

    public DummyViewModel(string title) => Title = title;
}
