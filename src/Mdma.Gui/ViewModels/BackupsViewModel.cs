using System.Collections.ObjectModel;
using Mdma.Core;

namespace Mdma.Gui.ViewModels;

public sealed class BackupHandleViewModel : ObservableObject
{
    public BackupHandle Handle { get; }

    public string Id => Handle.Id;
    public TargetApp Target => Handle.Target;
    public DateTime CreatedAtLocal => Handle.CreatedAt.ToLocalTime().DateTime;
    public string FormattedCreatedAt => CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
    public string StoragePath => Handle.StoragePath;

    public BackupHandleViewModel(BackupHandle handle)
    {
        Handle = handle;
    }
}

public sealed class BackupsViewModel : ObservableObject
{
    private readonly IBackupManager _backupManager;
    private readonly IRevertManager _revertManager;
    private readonly WorkingRoot _workingRoot;
    private TargetApp? _selectedTargetFilter;
    private BackupHandleViewModel? _selectedBackup;
    private bool _isReverting;
    private bool _hasResult;
    private bool _isSuccess;
    private string? _resultMessage;
    private string? _suggestedAction;

    public ObservableCollection<BackupHandleViewModel> Backups { get; } = new();

    public TargetApp? SelectedTargetFilter
    {
        get => _selectedTargetFilter;
        set
        {
            if (SetProperty(ref _selectedTargetFilter, value))
            {
                RefreshBackups();
            }
        }
    }

    public BackupHandleViewModel? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                RevertSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsReverting
    {
        get => _isReverting;
        set
        {
            if (SetProperty(ref _isReverting, value))
            {
                RevertSelectedCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
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

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RevertSelectedCommand { get; }

    public BackupsViewModel(
        IBackupManager backupManager,
        IRevertManager revertManager,
        WorkingRoot workingRoot
    )
    {
        _backupManager = backupManager;
        _revertManager = revertManager;
        _workingRoot = workingRoot;

        RefreshCommand = new RelayCommand(RefreshBackups, () => !IsReverting);
        RevertSelectedCommand = new RelayCommand(
            RunRevert,
            () => SelectedBackup is not null && !IsReverting
        );
    }

    public void RefreshBackups()
    {
        Backups.Clear();
        var listResult = _backupManager.ListBackups(_workingRoot, SelectedTargetFilter);
        if (listResult.IsSuccess)
        {
            foreach (var handle in listResult.Value!)
            {
                Backups.Add(new BackupHandleViewModel(handle));
            }
        }
    }

    public void RunRevert()
    {
        if (SelectedBackup is null)
            return;

        IsReverting = true;
        HasResult = false;

        var revertResult = _revertManager.Revert(SelectedBackup.Handle);

        IsReverting = false;
        HasResult = true;

        if (revertResult.IsSuccess)
        {
            IsSuccess = true;
            ResultMessage = $"Successfully restored backup snapshot '{SelectedBackup.Id}'.";
            SuggestedAction = null;
            RefreshBackups();
        }
        else
        {
            IsSuccess = false;
            ResultMessage = revertResult.Error!.Message;
            SuggestedAction = revertResult.Error.SuggestedAction;
        }
    }
}
