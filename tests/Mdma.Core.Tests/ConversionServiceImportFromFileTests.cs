using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class ConversionServiceImportFromFileTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;
    private string _validMdmaPath = null!;
    private FakeProcessLister _processLister = null!;
    private FakeDiskSpaceSource _diskSpace = null!;
    private FakeBackupManager _backupManager = null!;
    private FakeMdmaLoader _mdmaLoader = null!;
    private FakeDownloadListInjector _ndmInjector = null!;
    private FakeDownloadListInjector _jd2Injector = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-conversionservice-import-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, true, false);

        _validMdmaPath = new MdmaFixtureBuilder()
            .WithTotalBytes(100)
            .WithChunk(0, 0, 99, new byte[100])
            .BuildValid(Path.Combine(_testDir, "valid.mdma"));

        _processLister = new FakeProcessLister();
        _diskSpace = new FakeDiskSpaceSource { FreeBytes = long.MaxValue };
        _backupManager = new FakeBackupManager();
        _mdmaLoader = new FakeMdmaLoader();
        _ndmInjector = new FakeDownloadListInjector { TargetApp = TargetApp.NDM };
        _jd2Injector = new FakeDownloadListInjector { TargetApp = TargetApp.JD2 };
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
        _backupManager,
        exporters: null!, // not used by ImportFromFile
        injectors: new Dictionary<TargetApp, IDownloadListInjector> { [TargetApp.NDM] = _ndmInjector, [TargetApp.JD2] = _jd2Injector },
        mdmaLoader: _mdmaLoader);

    private static TargetAppLocation MakeNdmLocation() =>
        new(TargetApp.NDM, "/ndm/temp", "/ndm/meta", DownloadDirectory: "/ndm/downloads", WasAutoDetected: true);

    private static TargetAppLocation MakeJd2Location() =>
        new(TargetApp.JD2, "/jd2/cfg", MetadataDir: null, DownloadDirectory: "/jd2/downloads", WasAutoDetected: true);

    [Test]
    public void ImportFromFile_Succeeds_Full_Happy_Path()
    {
        var service = CreateService();

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_backupManager.CreateBackupCallCount, Is.EqualTo(1));
        Assert.That(_mdmaLoader.CallCount, Is.EqualTo(1));
        Assert.That(_ndmInjector.CallCount, Is.EqualTo(1));
        Assert.That(_jd2Injector.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ImportFromFile_Calls_Jd2Injector_When_Destination_Is_Jd2()
    {
        var service = CreateService();

        service.ImportFromFile(_validMdmaPath, MakeJd2Location());

        Assert.That(_jd2Injector.CallCount, Is.EqualTo(1));
        Assert.That(_ndmInjector.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ImportFromFile_Aborts_Before_Backup_When_Process_Running()
    {
        _processLister.RunningProcesses.Add("NeatDownloadManager.exe");
        var service = CreateService();

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppProcessRunning));
        Assert.That(_backupManager.CreateBackupCallCount, Is.EqualTo(0));
        Assert.That(_mdmaLoader.CallCount, Is.EqualTo(0));
        Assert.That(_ndmInjector.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ImportFromFile_Aborts_Before_Backup_When_Insufficient_Space()
    {
        _diskSpace.FreeBytes = 1;
        var service = CreateService();

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InsufficientDiskSpaceDestination));
        Assert.That(_backupManager.CreateBackupCallCount, Is.EqualTo(0));
        Assert.That(_mdmaLoader.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ImportFromFile_Aborts_Before_Load_When_Backup_Fails()
    {
        _backupManager.CreateBackupResultToReturn = new MdmaError(MdmaErrorCode.BackupFailed, "simulated backup failure");
        var service = CreateService();

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.BackupFailed));
        Assert.That(_mdmaLoader.CallCount, Is.EqualTo(0), "loader must not run if backup (Critical step) fails");
        Assert.That(_ndmInjector.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ImportFromFile_Aborts_Before_Inject_When_Load_Fails()
    {
        _mdmaLoader.ResultToReturn = new MdmaError(MdmaErrorCode.MdmaChecksumMismatch, "simulated corrupt package");
        var service = CreateService();

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaChecksumMismatch));
        Assert.That(_ndmInjector.CallCount, Is.EqualTo(0), "injector must not run if load fails");
    }

    [Test]
    public void ImportFromFile_Propagates_Injector_Failure()
    {
        _ndmInjector.ResultToReturn = new MdmaError(MdmaErrorCode.InjectionFailed, "simulated injector failure");
        var service = CreateService();

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }

    [Test]
    public void ImportFromFile_Fails_Cleanly_When_Mdma_File_Does_Not_Exist()
    {
        var service = CreateService();

        var result = service.ImportFromFile(Path.Combine(_testDir, "nope.mdma"), MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.MdmaFileNotFound));
        Assert.That(_backupManager.CreateBackupCallCount, Is.EqualTo(0));
    }

    [Test]
    public void ImportFromFile_Returns_Error_When_No_Injector_Registered_For_Destination()
    {
        var service = new ConversionService(
            _workingRoot,
            new ProcessGuard(_processLister),
            new SpaceChecker(_diskSpace),
            _backupManager,
            exporters: null!,
            injectors: new Dictionary<TargetApp, IDownloadListInjector>(), // empty
            mdmaLoader: _mdmaLoader);

        var result = service.ImportFromFile(_validMdmaPath, MakeNdmLocation());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }
}
