using System.IO;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using Mdma.Gui.ViewModels;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class ConvertViewModelTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-guiconvert-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void ConvertViewModel_Task_Setter_Defaults_TargetApp_To_Opposite()
    {
        var fakeConversion = new FakeConversionService();
        var resolver = new LocationResolver(new FakeRegistryAccessor(), appDataDirectory: _testDir);

        var vm = new ConvertViewModel(fakeConversion, resolver);

        vm.Task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            100,
            50,
            "Paused",
            true
        );
        Assert.That(vm.TargetApp, Is.EqualTo(TargetApp.JD2));

        vm.Task = new DownloadTaskSummary(
            "99_00",
            TargetApp.JD2,
            "f2.bin",
            "https://example.com/f2.bin",
            100,
            50,
            "Paused",
            true
        );
        Assert.That(vm.TargetApp, Is.EqualTo(TargetApp.NDM));
    }

    [Test]
    public void ConvertViewModel_RunConvert_Succeeds_And_Sets_Result()
    {
        var fakeConversion = new FakeConversionService();

        var ndmAppDataRoot = Path.Combine(_testDir, "ndm", "NeatDM");
        Directory.CreateDirectory(ndmAppDataRoot);
        var ndmFixture = new NdmFixtureBuilder(ndmAppDataRoot).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            100,
            (0, 99, 50)
        );
        ndmFixture.Build();

        var jd2Root = Path.Combine(_testDir, "jd2", "JDownloader 2");
        Directory.CreateDirectory(jd2Root);
        var jd2Fixture = new Jd2FixtureBuilder(jd2Root).WithLink(
            "99",
            "00",
            "f.bin",
            "https://example.com/f.bin",
            100,
            50,
            50
        );
        jd2Fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", ndmFixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", ndmFixture.DownloadDirectory);

        var resolver = new LocationResolver(
            fakeRegistry,
            appDataDirectory: Path.Combine(_testDir, "ndm"),
            localAppDataDirectory: Path.Combine(_testDir, "jd2")
        );

        var vm = new ConvertViewModel(fakeConversion, resolver)
        {
            Task = new DownloadTaskSummary(
                "1",
                TargetApp.NDM,
                "f.bin",
                "https://example.com/f.bin",
                100,
                50,
                "Paused",
                true
            ),
        };

        vm.RunConvert();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.True);
        Assert.That(fakeConversion.ConvertSameMachineCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ConvertViewModel_RunConvert_Fails_Fast_When_SourceLocation_Fails()
    {
        var fakeConversion = new FakeConversionService();
        var resolver = new LocationResolver(new FakeRegistryAccessor(), appDataDirectory: _testDir); // auto-detect fails

        var vm = new ConvertViewModel(fakeConversion, resolver)
        {
            Task = new DownloadTaskSummary(
                "1",
                TargetApp.NDM,
                "f.bin",
                "https://example.com/f.bin",
                100,
                50,
                "Paused",
                true
            ),
        };

        vm.RunConvert();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(
            fakeConversion.ConvertSameMachineCallCount,
            Is.EqualTo(0),
            "ConvertSameMachine must not be called when location fails to resolve."
        );
    }
}
