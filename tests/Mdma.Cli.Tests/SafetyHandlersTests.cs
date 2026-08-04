using Mdma.Cli.Handlers;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using NUnit.Framework;

namespace Mdma.Cli.Tests;

public class SafetyHandlersTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-clisafety-test-" + Guid.NewGuid());
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
    public void Backups_Lists_Snapshots_Successfully()
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
        backupManager.CreateBackup(location, _workingRoot, taskNativeId: "1");

        var args = new CliArgs(
            "backups",
            _testDir,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

        var exitCode = BackupsHandler.Execute(args, backupManagerOverride: backupManager);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }

    [Test]
    public void Revert_Restores_Snapshot_Successfully()
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

        // Corrupt live neatdb.db
        File.WriteAllText(fixture.NeatDbPath, "corrupt DB");

        var args = new CliArgs(
            "revert",
            _testDir,
            false,
            false,
            false,
            null,
            null,
            null,
            backup.Id,
            null,
            null,
            null,
            null,
            null,
            null
        );

        var revertManager = new RevertManager(
            new ProcessGuard(new FakeProcessLister()),
            new AtomicWriter()
        );

        var exitCode = RevertHandler.Execute(
            args,
            backupManagerOverride: backupManager,
            revertManagerOverride: revertManager
        );

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(File.ReadAllText(fixture.NeatDbPath), Is.Not.EqualTo("corrupt DB"));
    }

    [Test]
    public void Revert_Fails_When_Snapshot_Id_Not_Found()
    {
        var args = new CliArgs(
            "revert",
            _testDir,
            false,
            false,
            false,
            null,
            null,
            null,
            "nonexistent-id",
            null,
            null,
            null,
            null,
            null,
            null
        );

        var exitCode = RevertHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.SafetyOrBackupError));
    }

    [Test]
    public void Revert_Fails_When_Required_Id_Flag_Missing()
    {
        var args = new CliArgs(
            "revert",
            null,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );

        var exitCode = RevertHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.TargetAppNotFoundOrPathInvalid));
    }
}
