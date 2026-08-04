using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class RevertManagerTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-revertmanager-test-" + Guid.NewGuid());
        var workRootPath = Path.Combine(_testDir, "workroot");
        Directory.CreateDirectory(workRootPath);
        _workingRoot = new WorkingRoot(workRootPath, IsPortableDefault: true, IsFallback: false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private static RevertManager CreateManager(FakeProcessLister? lister = null) =>
        new(new ProcessGuard(lister ?? new FakeProcessLister()), new AtomicWriter());

    [Test]
    public void Revert_Restores_NeatDb_To_PreBackup_Content()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var originalBytes = File.ReadAllBytes(fixture.NeatDbPath);

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        // Simulate corruption/mutation after the backup was taken.
        File.WriteAllText(fixture.NeatDbPath, "corrupted, must be overwritten by revert");

        var revertManager = CreateManager();
        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.ReadAllBytes(fixture.NeatDbPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public void Revert_Restores_Task_Segment_Files_Too()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var seg0Path = Path.Combine(fixture.TempDirectory, "1", "seg.x0");
        var originalSegBytes = File.ReadAllBytes(seg0Path);

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        File.WriteAllBytes(seg0Path, new byte[] { 1, 2, 3 }); // mutate after backup

        var revertManager = CreateManager();
        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.ReadAllBytes(seg0Path), Is.EqualTo(originalSegBytes));
    }

    [Test]
    public void Revert_Restores_Jd2_Zip_To_PreBackup_Content()
    {
        var fixture = new Jd2FixtureBuilder(_testDir, counter: 5).WithLink(
            "99",
            "00",
            "f.bin",
            "https://example.com/f.bin",
            1000,
            200,
            200
        );
        var zipPath = fixture.Build();
        var originalBytes = File.ReadAllBytes(zipPath);

        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot).Value!;

        File.WriteAllText(zipPath, "corrupted zip content");

        var revertManager = CreateManager();
        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.ReadAllBytes(zipPath), Is.EqualTo(originalBytes));
    }

    [Test]
    public void Revert_Blocked_When_Ndm_Process_Running()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        var runningLister = new FakeProcessLister();
        runningLister.RunningProcesses.Add("NeatDownloadManager.exe");
        var revertManager = CreateManager(runningLister);

        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppProcessRunning));
    }

    [Test]
    public void Revert_Does_Not_Modify_Original_File_When_Process_Running()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        File.WriteAllText(fixture.NeatDbPath, "should remain untouched");

        var runningLister = new FakeProcessLister();
        runningLister.RunningProcesses.Add("NeatDownloadManager.exe");
        var revertManager = CreateManager(runningLister);
        revertManager.Revert(backup);

        Assert.That(File.ReadAllText(fixture.NeatDbPath), Is.EqualTo("should remain untouched"));
    }

    [Test]
    public void Revert_Fails_When_Snapshot_File_Tampered_And_Restores_Nothing()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        // Tamper with the backed-up neatdb.db copy itself (not the live file).
        File.WriteAllText(
            Path.Combine(backup.StoragePath, "neatdb.db"),
            "tampered snapshot content"
        );

        // Mutate the live file too, so we can prove it was NOT restored from the tampered snapshot.
        File.WriteAllText(fixture.NeatDbPath, "live file, should remain exactly this");

        var revertManager = CreateManager();
        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.RevertFailed));
        Assert.That(
            File.ReadAllText(fixture.NeatDbPath),
            Is.EqualTo("live file, should remain exactly this")
        );
    }

    [Test]
    public void Revert_Fails_When_Snapshot_File_Missing_From_Disk()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        File.Delete(Path.Combine(backup.StoragePath, "neatdb.db"));

        var revertManager = CreateManager();
        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.RevertFailed));
    }

    [Test]
    public void Revert_Fails_Cleanly_When_Manifest_Missing()
    {
        var fakeStoragePath = Path.Combine(_testDir, "nonexistent-backup");
        var handle = new BackupHandle(
            "fake-id",
            TargetApp.NDM,
            DateTimeOffset.UtcNow,
            fakeStoragePath
        );

        var revertManager = CreateManager();
        var result = revertManager.Revert(handle);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.RevertTargetNotFound));
    }

    [Test]
    public void Revert_Multi_File_Ndm_Backup_Restores_All_Files_Atomically()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            2000,
            (0, 999, 500),
            (1000, 1999, 500)
        );
        fixture.Build();
        var seg0Path = Path.Combine(fixture.TempDirectory, "1", "seg.x0");
        var seg1Path = Path.Combine(fixture.TempDirectory, "1", "seg.x1");
        var originalSeg0 = File.ReadAllBytes(seg0Path);
        var originalSeg1 = File.ReadAllBytes(seg1Path);

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        File.WriteAllBytes(seg0Path, new byte[] { 9 });
        File.WriteAllBytes(seg1Path, new byte[] { 9 });

        var revertManager = CreateManager();
        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.ReadAllBytes(seg0Path), Is.EqualTo(originalSeg0));
        Assert.That(File.ReadAllBytes(seg1Path), Is.EqualTo(originalSeg1));
    }

    [Test]
    public void Revert_Logs_Start_And_Success()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        var fakeLogger = new FakeMdmaLogger();
        var revertManager = new RevertManager(
            new ProcessGuard(new FakeProcessLister()),
            new AtomicWriter(),
            fakeLogger
        );

        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(
            fakeLogger.Entries.Any(e =>
                e.Level == MdmaLogLevel.Info && e.Message.Contains("Starting revert")
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

    [Test]
    public void Revert_Logs_Error_When_Process_Running()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var backupManager = new BackupManager(new FakeClock());
        var backup = backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1").Value!;

        var runningLister = new FakeProcessLister();
        runningLister.RunningProcesses.Add("NeatDownloadManager.exe");
        var fakeLogger = new FakeMdmaLogger();
        var revertManager = new RevertManager(
            new ProcessGuard(runningLister),
            new AtomicWriter(),
            fakeLogger
        );

        var result = revertManager.Revert(backup);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            fakeLogger.Entries.Any(e =>
                e.Level == MdmaLogLevel.Error && e.Message.Contains("Revert blocked")
            ),
            Is.True
        );
    }
}
