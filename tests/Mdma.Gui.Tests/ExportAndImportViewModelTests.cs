using System.IO;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using Mdma.Gui.ViewModels;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class ExportAndImportViewModelTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-guiexpimp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void ExportViewModel_RunExport_Succeeds_And_Sets_Result()
    {
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "file.bin",
            "https://example.com/file.bin",
            100,
            50,
            "Paused",
            true
        );
        var fakeConversion = new FakeConversionService();

        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "file.bin",
            "https://example.com/file.bin",
            100,
            (0, 99, 50)
        );
        fixture.Build();
        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var resolver = new LocationResolver(fakeRegistry, appDataDirectory: _testDir);

        var vm = new ExportViewModel(fakeConversion, resolver)
        {
            Task = task,
            DestinationPath = Path.Combine(_testDir, "exported.mdma"),
        };

        vm.RunExport();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.True);
        Assert.That(fakeConversion.ExportToFileCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ExportViewModel_RunExport_Handles_Failure_Gracefully()
    {
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "file.bin",
            "https://example.com/file.bin",
            100,
            50,
            "Paused",
            true
        );
        var fakeConversion = new FakeConversionService
        {
            ExportToFileResultToReturn = new MdmaError(
                MdmaErrorCode.ExportFailed,
                "Disk read error."
            ),
        };

        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "file.bin",
            "https://example.com/file.bin",
            100,
            (0, 99, 50)
        );
        fixture.Build();
        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var resolver = new LocationResolver(fakeRegistry, appDataDirectory: _testDir);

        var vm = new ExportViewModel(fakeConversion, resolver)
        {
            Task = task,
            DestinationPath = Path.Combine(_testDir, "exported.mdma"),
        };

        vm.RunExport();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(vm.ResultMessage, Is.EqualTo("Disk read error."));
    }

    [Test]
    public void ImportViewModel_RunImport_Succeeds_And_Sets_Result()
    {
        var fakeConversion = new FakeConversionService();
        var fixture = new NdmFixtureBuilder(_testDir);
        fixture.Build();
        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var resolver = new LocationResolver(fakeRegistry, appDataDirectory: _testDir);

        var vm = new ImportViewModel(fakeConversion, resolver)
        {
            PackageFilePath = Path.Combine(_testDir, "package.mdma"),
            TargetApp = TargetApp.NDM,
        };

        vm.RunImport();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.True);
        Assert.That(fakeConversion.ImportFromFileCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ImportViewModel_RunImport_Fails_Fast_When_TargetApp_Not_Found()
    {
        var fakeConversion = new FakeConversionService();
        var resolver = new LocationResolver(new FakeRegistryAccessor(), appDataDirectory: _testDir); // auto-detect fails

        var vm = new ImportViewModel(fakeConversion, resolver)
        {
            PackageFilePath = Path.Combine(_testDir, "package.mdma"),
            TargetApp = TargetApp.NDM,
        };

        vm.RunImport();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(
            fakeConversion.ImportFromFileCallCount,
            Is.EqualTo(0),
            "ImportFromFile must not be called when target app location fails to resolve."
        );
    }
}
