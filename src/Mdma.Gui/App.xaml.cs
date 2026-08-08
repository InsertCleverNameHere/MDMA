using System.Windows;
using Mdma.Core;
using Mdma.Gui.ViewModels;

namespace Mdma.Gui;

public partial class App : Application
{
    public static IConversionService ConversionService { get; private set; } = null!;
    public static IWorkingDirectoryProvider WorkingDirectoryProvider { get; private set; } = null!;
    public static WorkingRoot WorkingRoot { get; private set; } = null!;
    public static ShellViewModel ShellViewModel { get; private set; } = null!;
    public static ScanViewModel ScanViewModel { get; private set; } = null!;
    public static ExportViewModel ExportViewModel { get; private set; } = null!;
    public static ImportViewModel ImportViewModel { get; private set; } = null!;
    public static ConvertViewModel ConvertViewModel { get; private set; } = null!;
    public static BackupsViewModel BackupsViewModel { get; private set; } = null!;
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        WorkingDirectoryProvider = new WorkingDirectoryProvider();
        var registry = new RegistryAccessor();
        var resolver = new LocationResolver(registry);
        var ndmReader = new NdmListReader();
        var jd2Reader = new Jd2ListReader();

        ScanViewModel = new ScanViewModel(
            new NdmLocator(registry),
            new Jd2Locator(),
            ndmReader,
            jd2Reader
        );

        var workDirResult = WorkingDirectoryProvider.Resolve(null);
        if (workDirResult.IsSuccess)
        {
            WorkingRoot = workDirResult.Value!;
            var processGuard = new ProcessGuard(new ProcessLister());
            var spaceChecker = new SpaceChecker(new DiskSpaceSource());
            var backupManager = new BackupManager(new RealClock());
            var fileLogger = new FileLogger(WorkingRoot);

            ConversionService = new ConversionService(
                WorkingRoot,
                processGuard,
                spaceChecker,
                backupManager,
                exporters: new Dictionary<TargetApp, IMdmaExporter>
                {
                    [TargetApp.NDM] = new NdmExporter(),
                    [TargetApp.JD2] = new Jd2Exporter(),
                },
                injectors: new Dictionary<TargetApp, IDownloadListInjector>
                {
                    [TargetApp.NDM] = new NdmInjector(registry, new AtomicWriter()),
                    [TargetApp.JD2] = new Jd2Injector(new AtomicWriter()),
                },
                mdmaLoader: new MdmaLoader(),
                logger: fileLogger
            );

            var revertManager = new RevertManager(processGuard, new AtomicWriter(), fileLogger);
            BackupsViewModel = new BackupsViewModel(backupManager, revertManager, WorkingRoot);

            var cleanupService = new TempCleanupService(fileLogger);
            var settingsStore = new GuiSettingsStore();
            SettingsViewModel = new SettingsViewModel(cleanupService, settingsStore, WorkingRoot);

            ScanViewModel.RunScan();
        }

        ExportViewModel = new ExportViewModel(ConversionService!, resolver);
        ImportViewModel = new ImportViewModel(ConversionService!, resolver);
        ConvertViewModel = new ConvertViewModel(ConversionService!, resolver);

        var navService = new NavigationService();
        ShellViewModel = new ShellViewModel(
            navService,
            ScanViewModel,
            ExportViewModel,
            ImportViewModel,
            ConvertViewModel,
            BackupsViewModel,
            SettingsViewModel
        );
        ShellViewModel.InitializeWorkingRoot(WorkingDirectoryProvider);

        var mainWindow = new MainWindow { DataContext = ShellViewModel };
        mainWindow.Show();
    }
}
