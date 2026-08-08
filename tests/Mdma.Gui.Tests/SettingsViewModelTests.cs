using System.IO;
using Mdma.Core;
using Mdma.Gui;
using Mdma.Gui.ViewModels;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class SettingsViewModelTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-guisettings-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, IsPortableDefault: true, IsFallback: false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void GuiSettingsStore_Saves_And_Loads_Settings_Successfully()
    {
        var store = new GuiSettingsStore();
        var originalSettings = new GuiSettings(@"C:\CustomWorkDir");

        store.SaveSettings(_workingRoot, originalSettings);
        var loadedSettings = store.LoadSettings(_workingRoot);

        Assert.That(loadedSettings.WorkingRootOverride, Is.EqualTo(@"C:\CustomWorkDir"));
    }

    [Test]
    public void GuiSettingsStore_Returns_Default_Settings_When_File_Missing_Or_Corrupt()
    {
        var store = new GuiSettingsStore();

        var missingSettings = store.LoadSettings(_workingRoot);
        Assert.That(missingSettings.WorkingRootOverride, Is.Null);

        // Corrupt file test
        var path = Path.Combine(_testDir, "gui-settings.json");
        File.WriteAllText(path, "invalid json content");

        var corruptSettings = store.LoadSettings(_workingRoot);
        Assert.That(corruptSettings.WorkingRootOverride, Is.Null);
    }

    [Test]
    public void RunSweepOrphans_Invokes_TempCleanupService_And_Sets_Result_Summary()
    {
        var tmpDir = Path.Combine(_testDir, ".mdma-tmp");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllText(Path.Combine(tmpDir, "leftover.mdma"), "leftover");

        var cleanupService = new TempCleanupService();
        var store = new GuiSettingsStore();

        var vm = new SettingsViewModel(cleanupService, store, _workingRoot);

        vm.RunSweepOrphans();

        Assert.That(vm.HasSweepResult, Is.True);
        Assert.That(vm.RemovedCount, Is.EqualTo(1));
        Assert.That(vm.SweepSummary, Does.Contain("1 item(s) removed"));
        Assert.That(Directory.EnumerateFileSystemEntries(tmpDir), Is.Empty);
    }
}
