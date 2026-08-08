using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using Mdma.Gui;
using Mdma.Gui.ViewModels;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class ShellViewModelTests
{
    private sealed class FakeWorkingDirectoryProvider : IWorkingDirectoryProvider
    {
        public Result<WorkingRoot> ResultToReturn { get; set; } =
            Result<WorkingRoot>.Ok(
                new WorkingRoot(@"C:\MDMA_Work", IsPortableDefault: true, IsFallback: false)
            );

        public Result<WorkingRoot> Resolve(string? explicitOverride) => ResultToReturn;
    }

    private static ShellViewModel CreateDummyShellVm()
    {
        var nav = new NavigationService();
        var reg = new FakeRegistryAccessor();
        var workRoot = new WorkingRoot(@"C:\TestWork", true, false);
        var scanVm = new ScanViewModel(
            new NdmLocator(reg),
            new Jd2Locator(),
            new FakeDownloadListReader(),
            new FakeDownloadListReader()
        );
        var expVm = new ExportViewModel(new FakeConversionService(), new LocationResolver(reg));
        var impVm = new ImportViewModel(new FakeConversionService(), new LocationResolver(reg));
        var cvtVm = new ConvertViewModel(new FakeConversionService(), new LocationResolver(reg));
        var bkVm = new BackupsViewModel(
            new FakeBackupManager(),
            new RevertManager(new ProcessGuard(new FakeProcessLister()), new AtomicWriter()),
            workRoot
        );
        var setVm = new SettingsViewModel(
            new TempCleanupService(),
            new GuiSettingsStore(),
            workRoot
        );
        return new ShellViewModel(nav, scanVm, expVm, impVm, cvtVm, bkVm, setVm);
    }

    [Test]
    public void InitializeWorkingRoot_Sets_Path_And_FallbackFlag_On_Success()
    {
        var vm = CreateDummyShellVm();
        var provider = new FakeWorkingDirectoryProvider
        {
            ResultToReturn = Result<WorkingRoot>.Ok(
                new WorkingRoot(@"C:\Fallback\Path", IsPortableDefault: false, IsFallback: true)
            ),
        };

        vm.InitializeWorkingRoot(provider);

        Assert.That(vm.WorkingRootPath, Is.EqualTo(@"C:\Fallback\Path"));
        Assert.That(vm.IsFallbackWorkingRoot, Is.True);
        Assert.That(vm.HasBlockingError, Is.False);
    }

    [Test]
    public void InitializeWorkingRoot_Sets_HasBlockingError_On_Failure()
    {
        var vm = CreateDummyShellVm();
        var provider = new FakeWorkingDirectoryProvider
        {
            ResultToReturn = new MdmaError(
                MdmaErrorCode.WorkingDirectoryUnwritable,
                "Unwritable directory.",
                SuggestedAction: "Choose another path."
            ),
        };

        vm.InitializeWorkingRoot(provider);

        Assert.That(vm.HasBlockingError, Is.True);
        Assert.That(vm.ErrorMessage, Is.EqualTo("Unwritable directory."));
        Assert.That(vm.SuggestedAction, Is.EqualTo("Choose another path."));
    }

    [Test]
    public void IsBusy_Disables_NavigateCommand()
    {
        var vm = CreateDummyShellVm();

        Assert.That(vm.NavigateCommand.CanExecute(ViewScreen.Backups), Is.True);

        vm.IsBusy = true;

        Assert.That(vm.NavigateCommand.CanExecute(ViewScreen.Backups), Is.False);
    }

    [Test]
    public void DismissWarningCommand_Clears_IsFallbackWorkingRoot()
    {
        var vm = CreateDummyShellVm();
        vm.IsFallbackWorkingRoot = true;

        vm.DismissWarningCommand.Execute(null);

        Assert.That(vm.IsFallbackWorkingRoot, Is.False);
    }

    [Test]
    public void NavigateCommand_Updates_NavigationService()
    {
        var vm = CreateDummyShellVm();

        vm.NavigateCommand.Execute(ViewScreen.Settings);

        Assert.That(vm.NavigationService.CurrentScreen, Is.EqualTo(ViewScreen.Settings));
        Assert.That(vm.NavigationService.CurrentViewModel, Is.InstanceOf<SettingsViewModel>());
    }
}
