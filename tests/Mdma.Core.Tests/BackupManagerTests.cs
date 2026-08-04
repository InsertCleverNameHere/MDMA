using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class BackupManagerTests
{
    private string _testDir = null!;
    private string _workRootPath = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-backupmanager-test-" + Guid.NewGuid());
        _workRootPath = Path.Combine(_testDir, "workroot");
        Directory.CreateDirectory(_workRootPath);
        _workingRoot = new WorkingRoot(_workRootPath, IsPortableDefault: true, IsFallback: false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void CreateBackup_Ndm_Captures_NeatDb_And_Task_Directory()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
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
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot, taskNativeId: "521");

        Assert.That(result.IsSuccess, Is.True);
        var handle = result.Value!;
        Assert.That(File.Exists(Path.Combine(handle.StoragePath, "neatdb.db")), Is.True);
        Assert.That(
            File.Exists(Path.Combine(handle.StoragePath, "task", "521", "seg.x0")),
            Is.True
        );
        Assert.That(
            File.Exists(Path.Combine(handle.StoragePath, "task", "521", "segments.bin")),
            Is.True
        );
        Assert.That(File.Exists(Path.Combine(handle.StoragePath, "manifest.json")), Is.True);
    }

    [Test]
    public void CreateBackup_Ndm_Copies_Are_Byte_For_Byte_Identical()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();
        var originalDbBytes = File.ReadAllBytes(fixture.NeatDbPath);

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var manager = new BackupManager(new FakeClock());
        var result = manager.CreateBackup(location, _workingRoot, taskNativeId: "1");

        var backedUpDbBytes = File.ReadAllBytes(
            Path.Combine(result.Value!.StoragePath, "neatdb.db")
        );
        Assert.That(backedUpDbBytes, Is.EqualTo(originalDbBytes));
    }

    [Test]
    public void CreateBackup_Ndm_Without_TaskNativeId_Only_Backs_Up_Db()
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
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot); // no taskNativeId

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(Path.Combine(result.Value!.StoragePath, "neatdb.db")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(result.Value!.StoragePath, "task")), Is.False);
    }

    [Test]
    public void CreateBackup_Ndm_Succeeds_As_NoOp_When_NeatDb_Missing_FreshInstall()
    {
        var location = new TargetAppLocation(
            TargetApp.NDM,
            _testDir,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot);

        Assert.That(
            result.IsSuccess,
            Is.True,
            "a missing neatdb.db means a fresh install with nothing to protect -- this must be a no-op success, not a failure"
        );
        Assert.That(File.Exists(Path.Combine(result.Value!.StoragePath, "neatdb.db")), Is.False);
    }

    [Test]
    public void CreateBackup_Ndm_Succeeds_When_Task_Directory_Does_Not_Exist()
    {
        // Task not started yet -- db exists, but no temp folder for this id.
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
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(
            location,
            _workingRoot,
            taskNativeId: "999-does-not-exist"
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(Path.Combine(result.Value!.StoragePath, "neatdb.db")), Is.True);
    }

    [Test]
    public void CreateBackup_Jd2_Captures_Newest_Zip()
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
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(
            File.Exists(Path.Combine(result.Value!.StoragePath, "downloadList5.zip")),
            Is.True
        );
    }

    [Test]
    public void CreateBackup_Jd2_Picks_Newest_When_Multiple_Zips_Present()
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
        fixture.Build();
        fixture.BuildStaleDuplicate(1);

        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(
            File.Exists(Path.Combine(result.Value!.StoragePath, "downloadList5.zip")),
            Is.True
        );
        Assert.That(
            File.Exists(Path.Combine(result.Value!.StoragePath, "downloadList1.zip")),
            Is.False
        );
    }

    [Test]
    public void CreateBackup_Jd2_Succeeds_As_NoOp_When_No_Zips_Present_FreshInstall()
    {
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var location = new TargetAppLocation(
            TargetApp.JD2,
            cfgDir,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot);

        Assert.That(
            result.IsSuccess,
            Is.True,
            "no downloadList*.zip means a fresh install with nothing to protect -- this must be a no-op success, not a failure"
        );
    }

    [Test]
    public void ListBackups_Returns_Empty_When_No_Backups_Exist()
    {
        var manager = new BackupManager(new FakeClock());
        var result = manager.ListBackups(_workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public void ListBackups_Returns_Newest_First()
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

        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var manager = new BackupManager(clock);
        var first = manager.CreateBackup(location, _workingRoot);

        clock.UtcNow = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var second = manager.CreateBackup(location, _workingRoot);

        var listResult = manager.ListBackups(_workingRoot);

        Assert.That(listResult.IsSuccess, Is.True);
        Assert.That(listResult.Value, Has.Count.EqualTo(2));
        Assert.That(listResult.Value![0].Id, Is.EqualTo(second.Value!.Id));
        Assert.That(listResult.Value[1].Id, Is.EqualTo(first.Value!.Id));
    }

    [Test]
    public void ListBackups_Filters_By_Target()
    {
        var ndmFixture = new NdmFixtureBuilder(Path.Combine(_testDir, "ndm")).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        ndmFixture.Build();
        var ndmLocation = new TargetAppLocation(
            TargetApp.NDM,
            ndmFixture.TempDirectory,
            Path.Combine(_testDir, "ndm"),
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var jd2Fixture = new Jd2FixtureBuilder(Path.Combine(_testDir, "jd2")).WithLink(
            "99",
            "00",
            "f.bin",
            "https://example.com/f.bin",
            1000,
            200,
            200
        );
        jd2Fixture.Build();
        var jd2Location = new TargetAppLocation(
            TargetApp.JD2,
            jd2Fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var manager = new BackupManager(new FakeClock());
        manager.CreateBackup(ndmLocation, _workingRoot);
        manager.CreateBackup(jd2Location, _workingRoot);

        var ndmOnly = manager.ListBackups(_workingRoot, TargetApp.NDM);

        Assert.That(ndmOnly.Value, Has.Count.EqualTo(1));
        Assert.That(ndmOnly.Value![0].Target, Is.EqualTo(TargetApp.NDM));
    }

    [Test]
    public void CreateBackup_Does_Not_Leave_Partial_Directory_On_Failure()
    {
        // Missing neatdb.db/downloadList*.zip is now a no-op success (fresh
        // install), so force a genuine failure a different way: point at a
        // cfg\ directory that doesn't exist AT ALL, which makes
        // Directory.GetFiles throw rather than return zero results.
        var location = new TargetAppLocation(
            TargetApp.JD2,
            Path.Combine(_testDir, "does-not-exist"),
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var manager = new BackupManager(new FakeClock());

        var result = manager.CreateBackup(location, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.BackupFailed));

        var backupsDir = Path.Combine(_workRootPath, "backups");
        if (Directory.Exists(backupsDir))
        {
            Assert.That(Directory.GetDirectories(backupsDir), Is.Empty);
        }
    }

    [Test]
    public void CreateBackup_Logs_Start_And_Success()
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

        var fakeLogger = new FakeMdmaLogger();
        var manager = new BackupManager(new FakeClock(), fakeLogger);

        var result = manager.CreateBackup(location, _workingRoot, taskNativeId: "1");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fakeLogger.Entries, Has.Count.AtLeast(2));
        Assert.That(
            fakeLogger.Entries.Any(e =>
                e.Level == MdmaLogLevel.Info && e.Message.Contains("Starting backup")
            ),
            Is.True
        );
        Assert.That(
            fakeLogger.Entries.Any(e =>
                e.Level == MdmaLogLevel.Info && e.Message.Contains("created successfully")
            ),
            Is.True
        );
    }

    [Test]
    public void CreateBackup_Logs_Error_On_Failure()
    {
        var location = new TargetAppLocation(
            TargetApp.JD2,
            Path.Combine(_testDir, "does-not-exist"),
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var fakeLogger = new FakeMdmaLogger();
        var manager = new BackupManager(new FakeClock(), fakeLogger);

        var result = manager.CreateBackup(location, _workingRoot);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(fakeLogger.Entries.Any(e => e.Level == MdmaLogLevel.Error), Is.True);
    }
}
