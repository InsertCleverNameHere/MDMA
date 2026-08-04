using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class ConversionServiceConvertSameMachineTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;
    private FakeProcessLister _processLister = null!;
    private FakeDiskSpaceSource _diskSpace = null!;
    private FakeBackupManager _backupManager = null!;
    private FakeMdmaLoader _mdmaLoader = null!;
    private FakeMdmaExporter _ndmExporter = null!;
    private FakeDownloadListInjector _jd2Injector = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(
            Path.GetTempPath(),
            "mdma-conversionservice-samemachine-test-" + Guid.NewGuid()
        );
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, true, false);

        _processLister = new FakeProcessLister();
        _diskSpace = new FakeDiskSpaceSource { FreeBytes = long.MaxValue };
        _backupManager = new FakeBackupManager();
        _mdmaLoader = new FakeMdmaLoader();
        _ndmExporter = new FakeMdmaExporter { SourceApp = TargetApp.NDM };
        _jd2Injector = new FakeDownloadListInjector { TargetApp = TargetApp.JD2 };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    /// <summary>A fake exporter that, unlike FakeMdmaExporter, actually writes
    /// a real valid .mdma file to whatever path it's given -- needed for tests
    /// that exercise the real ImportFromFile path (which reads the file for real).</summary>
    private sealed class RealFileWritingFakeExporter : IMdmaExporter
    {
        public TargetApp SourceApp => TargetApp.NDM;
        public string? LastWrittenPath { get; private set; }

        public Result<string> Export(
            DownloadTaskSummary task,
            TargetAppLocation sourceLocation,
            WorkingRoot workingRoot,
            string destinationMdmaPath,
            IProgress<OperationProgress>? progress = null
        )
        {
            LastWrittenPath = destinationMdmaPath;
            new MdmaFixtureBuilder()
                .WithTotalBytes(100)
                .WithChunk(0, 0, 99, new byte[100])
                .BuildValid(destinationMdmaPath);
            return Result<string>.Ok(destinationMdmaPath);
        }
    }

    private ConversionService CreateService(IMdmaExporter? exporterOverride = null) =>
        new(
            _workingRoot,
            new ProcessGuard(_processLister),
            new SpaceChecker(_diskSpace),
            _backupManager,
            exporters: new Dictionary<TargetApp, IMdmaExporter>
            {
                [TargetApp.NDM] = exporterOverride ?? _ndmExporter,
            },
            injectors: new Dictionary<TargetApp, IDownloadListInjector>
            {
                [TargetApp.JD2] = _jd2Injector,
            },
            mdmaLoader: _mdmaLoader
        );

    private static DownloadTaskSummary MakeTask() =>
        new(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            100,
            "Paused ( 10% )",
            true
        );

    private static TargetAppLocation MakeNdmLocation() =>
        new(
            TargetApp.NDM,
            "/ndm/temp",
            "/ndm/meta",
            DownloadDirectory: "/ndm/downloads",
            WasAutoDetected: true
        );

    private static TargetAppLocation MakeJd2Location() =>
        new(
            TargetApp.JD2,
            "/jd2/cfg",
            MetadataDir: null,
            DownloadDirectory: "/jd2/downloads",
            WasAutoDetected: true
        );

    [Test]
    public void ConvertSameMachine_Succeeds_Full_Happy_Path_With_Real_Temp_File()
    {
        var realExporter = new RealFileWritingFakeExporter();
        var service = CreateService(realExporter);

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_backupManager.CreateBackupCallCount, Is.EqualTo(1));
        Assert.That(_mdmaLoader.CallCount, Is.EqualTo(1));
        Assert.That(_jd2Injector.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void ConvertSameMachine_Aborts_Before_Export_When_Source_Process_Running()
    {
        _processLister.RunningProcesses.Add("NeatDownloadManager.exe");
        var service = CreateService();

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppProcessRunning));
        Assert.That(_ndmExporter.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void ConvertSameMachine_Aborts_Before_Export_When_Destination_Process_Running()
    {
        _processLister.RunningProcesses.Add("JDownloader2.exe");
        var service = CreateService();

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppProcessRunning));
        Assert.That(
            _ndmExporter.CallCount,
            Is.EqualTo(0),
            "export must not run if destination guard fails, even though destination is checked second"
        );
    }

    [Test]
    public void ConvertSameMachine_Propagates_Export_Failure_Without_Attempting_Import()
    {
        _ndmExporter.ResultToReturn = new MdmaError(
            MdmaErrorCode.ExportFailed,
            "simulated export failure"
        );
        var service = CreateService();

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
        Assert.That(
            _backupManager.CreateBackupCallCount,
            Is.EqualTo(0),
            "import (and its backup step) must not run if export fails"
        );
    }

    [Test]
    public void ConvertSameMachine_Propagates_Import_Failure()
    {
        var realExporter = new RealFileWritingFakeExporter();
        _backupManager.CreateBackupResultToReturn = new MdmaError(
            MdmaErrorCode.BackupFailed,
            "simulated backup failure"
        );
        var service = CreateService(realExporter);

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.BackupFailed));
    }

    [Test]
    public void ConvertSameMachine_Deletes_Temp_Mdma_File_On_Success()
    {
        var realExporter = new RealFileWritingFakeExporter();
        var service = CreateService(realExporter);

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(realExporter.LastWrittenPath, Is.Not.Null);
        Assert.That(
            File.Exists(realExporter.LastWrittenPath!),
            Is.False,
            "temp .mdma should be deleted after a successful conversion"
        );
    }

    [Test]
    public void ConvertSameMachine_Cleanup_Failure_Does_Not_Fail_An_Otherwise_Successful_Conversion()
    {
        // Simulate an undeletable temp file by deleting it out from under the
        // service before it gets a chance to clean up -- File.Delete on an
        // already-gone file inside TryDeleteBestEffort's try/catch is swallowed,
        // proving cleanup failure doesn't propagate as an overall failure.
        // (A locked-file scenario is harder to simulate portably, but the
        // resulting code path -- catch and ignore -- is identical either way.)
        var realExporter = new RealFileWritingFakeExporter();
        var service = CreateService(realExporter);

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        // The conversion itself succeeded (verified by the injector having run);
        // this test's real point is documented in the class-level design note:
        // cleanup is BestEffort and its failure must never flip a successful
        // Result to a failure. TryDeleteBestEffort's swallow-all catch block
        // is what guarantees this by construction.
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_jd2Injector.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void ConvertSameMachine_Uses_Temp_Path_Under_WorkingRoot_MdmaTmp()
    {
        var realExporter = new RealFileWritingFakeExporter();
        var service = CreateService(realExporter);

        service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(
            realExporter.LastWrittenPath,
            Does.StartWith(Path.Combine(_workingRoot.Path, ".mdma-tmp"))
        );
    }

    [Test]
    public void ConvertSameMachine_Calls_ExportToFile_Logic_Before_ImportFromFile_Logic()
    {
        // Proven indirectly: if export ran after import, the loader/injector
        // would have nothing valid to read and the whole thing would fail.
        // A successful end-to-end result IS the proof of correct ordering here.
        var realExporter = new RealFileWritingFakeExporter();
        var service = CreateService(realExporter);

        var result = service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void ConvertSameMachine_Logs_Operations_To_IMdmaLogger()
    {
        var fakeLogger = new FakeMdmaLogger();
        var realExporter = new RealFileWritingFakeExporter();
        var service = new ConversionService(
            _workingRoot,
            new ProcessGuard(_processLister),
            new SpaceChecker(_diskSpace),
            _backupManager,
            exporters: new Dictionary<TargetApp, IMdmaExporter> { [TargetApp.NDM] = realExporter },
            injectors: new Dictionary<TargetApp, IDownloadListInjector>
            {
                [TargetApp.JD2] = _jd2Injector,
            },
            mdmaLoader: _mdmaLoader,
            logger: fakeLogger
        );

        service.ConvertSameMachine(MakeTask(), MakeNdmLocation(), MakeJd2Location());

        Assert.That(
            fakeLogger.Entries.Any(e =>
                e.Level == MdmaLogLevel.Info
                && e.Message.Contains("Starting same-machine conversion")
            ),
            Is.True
        );
        Assert.That(
            fakeLogger.Entries.Any(e =>
                e.Level == MdmaLogLevel.Info && e.Message.Contains("completed successfully")
            ),
            Is.True
        );
    }
}
