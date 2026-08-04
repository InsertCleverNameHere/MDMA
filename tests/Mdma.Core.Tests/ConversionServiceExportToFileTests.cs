using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class ConversionServiceExportToFileTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;
    private FakeProcessLister _processLister = null!;
    private FakeDiskSpaceSource _diskSpace = null!;
    private FakeMdmaExporter _ndmExporter = null!;
    private FakeMdmaExporter _jd2Exporter = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-conversionservice-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, true, false);

        _processLister = new FakeProcessLister();
        _diskSpace = new FakeDiskSpaceSource { FreeBytes = long.MaxValue };
        _ndmExporter = new FakeMdmaExporter { SourceApp = TargetApp.NDM };
        _jd2Exporter = new FakeMdmaExporter { SourceApp = TargetApp.JD2 };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private ConversionService CreateService() => new(
        _workingRoot,
        new ProcessGuard(_processLister),
        new SpaceChecker(_diskSpace),
        backupManager: null!, // not used by ExportToFile
        exporters: new Dictionary<TargetApp, IMdmaExporter> { [TargetApp.NDM] = _ndmExporter, [TargetApp.JD2] = _jd2Exporter },
        injectors: null!, // not used by ExportToFile
        mdmaLoader: null!); // not used by ExportToFile

    private static DownloadTaskSummary MakeTask(TargetApp source, long downloadedBytes = 100) =>
        new("1", source, "f.bin", "https://example.com/f.bin", 1000, downloadedBytes, "Paused ( 10% )", true);

    private static TargetAppLocation MakeLocation(TargetApp app) =>
        new(app, "/some/dir", "/some/meta", DownloadDirectory: null, WasAutoDetected: true);

    [Test]
    public void ExportToFile_Succeeds_And_Calls_Correct_Exporter_For_Source()
    {
        var service = CreateService();
        var task = MakeTask(TargetApp.NDM);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_ndmExporter.CallCount, Is.EqualTo(1));
        Assert.That(_jd2Exporter.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ExportToFile_Calls_Jd2Exporter_When_Source_Is_Jd2()
    {
        var service = CreateService();
        var task = MakeTask(TargetApp.JD2);

        service.ExportToFile(task, MakeLocation(TargetApp.JD2), Path.Combine(_testDir, "out.mdma"));

        Assert.That(_jd2Exporter.CallCount, Is.EqualTo(1));
        Assert.That(_ndmExporter.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ExportToFile_Aborts_Before_Export_When_Process_Is_Running()
    {
        _processLister.RunningProcesses.Add("NeatDownloadManager.exe");
        var service = CreateService();
        var task = MakeTask(TargetApp.NDM);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppProcessRunning));
        Assert.That(_ndmExporter.CallCount, Is.EqualTo(0), "exporter must not be called when process guard fails");
    }

    [Test]
    public void ExportToFile_Aborts_Before_Export_When_Insufficient_Space()
    {
        _diskSpace.FreeBytes = 1; // nowhere near enough
        var service = CreateService();
        var task = MakeTask(TargetApp.NDM, downloadedBytes: 1_000_000);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InsufficientDiskSpaceSource));
        Assert.That(_ndmExporter.CallCount, Is.EqualTo(0), "exporter must not be called when space check fails");
    }

    [Test]
    public void ExportToFile_Process_Guard_Checked_Before_Space_Check()
    {
        // Both conditions fail simultaneously -- process guard should be the
        // one that actually fires, proving it's checked first (space check
        // would report a different error code if it ran first).
        _processLister.RunningProcesses.Add("NeatDownloadManager.exe");
        _diskSpace.FreeBytes = 1;
        var service = CreateService();
        var task = MakeTask(TargetApp.NDM, downloadedBytes: 1_000_000);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppProcessRunning));
    }

    [Test]
    public void ExportToFile_Propagates_Exporter_Failure()
    {
        _ndmExporter.ResultToReturn = new MdmaError(MdmaErrorCode.ExportFailed, "simulated exporter failure");
        var service = CreateService();
        var task = MakeTask(TargetApp.NDM);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void ExportToFile_Returns_Error_When_No_Exporter_Registered_For_Source()
    {
        var service = new ConversionService(
            _workingRoot,
            new ProcessGuard(_processLister),
            new SpaceChecker(_diskSpace),
            backupManager: null!,
            exporters: new Dictionary<TargetApp, IMdmaExporter>(), // empty registry
            injectors: null!,
            mdmaLoader: null!);
        var task = MakeTask(TargetApp.NDM);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void ExportToFile_Returns_Path_From_Exporter_On_Success()
    {
        var expectedPath = Path.Combine(_testDir, "out.mdma");
        _ndmExporter.ResultToReturn = Result<string>.Ok(expectedPath);
        var service = CreateService();
        var task = MakeTask(TargetApp.NDM);

        var result = service.ExportToFile(task, MakeLocation(TargetApp.NDM), expectedPath);

        Assert.That(result.Value, Is.EqualTo(expectedPath));
    }
}
