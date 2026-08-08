namespace Mdma.Gui;

public enum ViewScreen
{
    Scan,
    Export,
    Import,
    Convert,
    Backups,
    Settings,
}

public interface INavigationService
{
    ViewScreen CurrentScreen { get; }
    ObservableObject? CurrentViewModel { get; }
    void NavigateTo(ViewScreen screen, ObservableObject viewModel);
}

public sealed class NavigationService : ObservableObject, INavigationService
{
    private ViewScreen _currentScreen = ViewScreen.Scan;
    private ObservableObject? _currentViewModel;

    public ViewScreen CurrentScreen
    {
        get => _currentScreen;
        private set => SetProperty(ref _currentScreen, value);
    }

    public ObservableObject? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public void NavigateTo(ViewScreen screen, ObservableObject viewModel)
    {
        CurrentScreen = screen;
        CurrentViewModel = viewModel;
    }
}
