using System.IO;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using Mdma.Gui.ViewModels;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class BackupsViewModelTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-guibackups-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, true, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void RefreshBackups_Populates_Backups_Collection_And_Formats_LocalTime()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            100,
            (0, 99, 50)
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
            UtcNow = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero),
        };
        var backupManager = new BackupManager(clock);
        backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1");

        var revertManager = new RevertManager(
            new ProcessGuard(new FakeProcessLister()),
            new AtomicWriter()
        );

        var vm = new BackupsViewModel(backupManager, revertManager, _workingRoot);

        vm.RefreshBackups();

        Assert.That(vm.Backups, Has.Count.EqualTo(1));
        Assert.That(vm.Backups[0].Target, Is.EqualTo(TargetApp.NDM));
        Assert.That(vm.Backups[0].FormattedCreatedAt, Does.Contain("2026-08-08"));
    }

    [Test]
    public void RefreshBackups_Filters_By_TargetApp_When_Set()
    {
        var ndmFixture = new NdmFixtureBuilder(Path.Combine(_testDir, "ndm")).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            100,
            (0, 99, 50)
        );
        ndmFixture.Build();
        var ndmLocation = new TargetAppLocation(
            TargetApp.NDM,
            ndmFixture.TempDirectory,
            Path.Combine(_testDir, "ndm"),
            null,
            true
        );

        var jd2Fixture = new Jd2FixtureBuilder(Path.Combine(_testDir, "jd2")).WithLink(
            "99",
            "00",
            "f.bin",
            "https://example.com/f.bin",
            100,
            50,
            50
        );
        jd2Fixture.Build();
        var jd2Location = new TargetAppLocation(
            TargetApp.JD2,
            jd2Fixture.CfgDirectory,
            null,
            null,
            true
        );

        var backupManager = new BackupManager(new FakeClock());
        backupManager.CreateBackup(ndmLocation, _workingRoot);
        backupManager.CreateBackup(jd2Location, _workingRoot);

        var revertManager = new RevertManager(
            new ProcessGuard(new FakeProcessLister()),
            new AtomicWriter()
        );

        var vm = new BackupsViewModel(backupManager, revertManager, _workingRoot);

        vm.SelectedTargetFilter = TargetApp.JD2;

        Assert.That(vm.Backups, Has.Count.EqualTo(1));
        Assert.That(vm.Backups[0].Target, Is.EqualTo(TargetApp.JD2));
    }

    [Test]
    public void RunRevert_Succeeds_And_Sets_Result()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            100,
            (0, 99, 50)
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

        var revertManager = new RevertManager(
            new ProcessGuard(new FakeProcessLister()),
            new AtomicWriter()
        );

        var vm = new BackupsViewModel(backupManager, revertManager, _workingRoot);
        vm.RefreshBackups();
        vm.SelectedBackup = vm.Backups[0];

        vm.RunRevert();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.True);
        Assert.That(vm.ResultMessage, Does.Contain("Successfully restored"));
    }

    [Test]
    public void RunRevert_Handles_Failure_Gracefully()
    {
        var fakeStoragePath = Path.Combine(_testDir, "fake-backup");
        var handle = new BackupHandle(
            "fake-id",
            TargetApp.NDM,
            DateTimeOffset.UtcNow,
            fakeStoragePath
        );

        var fakeBackupManager = new FakeBackupManager();
        fakeBackupManager.ListBackupsResultToReturn = Result<IReadOnlyList<BackupHandle>>.Ok(
            new[] { handle }
        );

        var revertManager = new RevertManager(
            new ProcessGuard(new FakeProcessLister()),
            new AtomicWriter()
        );

        var vm = new BackupsViewModel(fakeBackupManager, revertManager, _workingRoot);
        vm.RefreshBackups();
        vm.SelectedBackup = vm.Backups[0];

        vm.RunRevert();

        Assert.That(vm.HasResult, Is.True);
        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(vm.ResultMessage, Does.Contain("manifest could not be found"));
    }
}
