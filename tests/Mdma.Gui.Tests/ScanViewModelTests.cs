using System.IO;
using System.Linq;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using Mdma.Gui.ViewModels;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class ScanViewModelTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-guiscan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void RunScan_Populates_Tasks_When_Targets_Found()
    {
        var ndmRoot = Path.Combine(_testDir, "ndm");
        var appDataRoot = Path.Combine(ndmRoot, "NeatDM");
        Directory.CreateDirectory(appDataRoot);

        var ndmFixture = new NdmFixtureBuilder(appDataRoot).WithTask(
            1,
            "ndm.bin",
            "https://example.com/ndm.bin",
            100,
            (0, 99, 50)
        );
        ndmFixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", ndmFixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", ndmFixture.DownloadDirectory);

        // Pass ndmRoot so NdmLocator looks in ndmRoot\NeatDM instead of live %APPDATA%\NeatDM
        var ndmLocator = new NdmLocator(fakeRegistry, appDataDirectory: ndmRoot);
        var jd2Locator = new Jd2Locator(Path.Combine(_testDir, "no-jd2"));
        var ndmReader = new NdmListReader();
        var jd2Reader = new FakeDownloadListReader();

        var vm = new ScanViewModel(ndmLocator, jd2Locator, ndmReader, jd2Reader);

        vm.RunScan();

        Assert.That(vm.Tasks, Has.Count.EqualTo(1));
        Assert.That(vm.Tasks[0].Filename, Is.EqualTo("ndm.bin"));
        Assert.That(vm.NdmStatus.IsFound, Is.True);
        Assert.That(vm.Jd2Status.IsFound, Is.False);
    }

    [Test]
    public void RunScan_Handles_Both_Targets_Missing_Without_Crashing()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        var ndmLocator = new NdmLocator(fakeRegistry, appDataDirectory: _testDir);
        var jd2Locator = new Jd2Locator(Path.Combine(_testDir, "no-jd2"));
        var ndmReader = new NdmListReader();
        var jd2Reader = new FakeDownloadListReader();

        var vm = new ScanViewModel(ndmLocator, jd2Locator, ndmReader, jd2Reader);

        vm.RunScan();

        Assert.That(vm.Tasks, Is.Empty);
        Assert.That(vm.NdmStatus.IsFound, Is.False);
        Assert.That(vm.Jd2Status.IsFound, Is.False);
    }

    [Test]
    public void SetManualPath_Triggers_Validation_And_Replaces_Tasks()
    {
        var ndmFixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "manual.bin",
            "https://example.com/manual.bin",
            100,
            (0, 99, 50)
        );
        ndmFixture.Build();

        var fakeRegistry = new FakeRegistryAccessor();
        var ndmLocator = new NdmLocator(
            fakeRegistry,
            appDataDirectory: Path.Combine(_testDir, "no-appdata")
        );
        var jd2Locator = new Jd2Locator(Path.Combine(_testDir, "no-jd2"));
        var ndmReader = new NdmListReader();
        var jd2Reader = new FakeDownloadListReader();

        var vm = new ScanViewModel(ndmLocator, jd2Locator, ndmReader, jd2Reader);
        vm.RunScan();

        Assert.That(vm.Tasks, Is.Empty);

        // Provide manual path to neatdb.db directory
        vm.SetManualPath(TargetApp.NDM, _testDir);

        Assert.That(vm.Tasks, Has.Count.EqualTo(1));
        Assert.That(vm.Tasks[0].Filename, Is.EqualTo("manual.bin"));
        Assert.That(vm.NdmStatus.IsFound, Is.True);
    }

    [Test]
    public void Refresh_Clears_And_Reloads_Tasks()
    {
        var fakeReader = new FakeDownloadListReader();
        fakeReader.ScanTasksResultToReturn = Result<IReadOnlyList<DownloadTaskSummary>>.Ok(
            new[]
            {
                new DownloadTaskSummary(
                    "1",
                    TargetApp.NDM,
                    "file1.bin",
                    "https://example.com/f1.bin",
                    100,
                    50,
                    "Paused",
                    true
                ),
            }
        );

        var neatDmDir = Path.Combine(_testDir, "NeatDM");
        Directory.CreateDirectory(neatDmDir);
        File.WriteAllText(Path.Combine(neatDmDir, "neatdb.db"), ""); // dummy file for locate

        var fakeNdmLocator = new NdmLocator(
            new FakeRegistryAccessor()
                .Seed(@"SOFTWARE\NeatDM", "TempDirectory", _testDir)
                .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", _testDir),
            appDataDirectory: _testDir
        );

        var vm = new ScanViewModel(
            fakeNdmLocator,
            new Jd2Locator(_testDir),
            fakeReader,
            fakeReader
        );

        vm.RunScan();
        Assert.That(vm.Tasks, Has.Count.EqualTo(1));

        vm.RunScan(); // Re-run refresh
        Assert.That(vm.Tasks, Has.Count.EqualTo(1));
    }

    private static ScanViewModel CreateDummyScanVm()
    {
        var reg = new FakeRegistryAccessor();
        return new ScanViewModel(
            new NdmLocator(reg),
            new Jd2Locator(),
            new FakeDownloadListReader(),
            new FakeDownloadListReader()
        );
    }
}
