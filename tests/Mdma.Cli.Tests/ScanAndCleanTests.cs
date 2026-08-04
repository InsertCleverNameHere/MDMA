using Mdma.Cli.Handlers;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using NUnit.Framework;

namespace Mdma.Cli.Tests;

public class ScanAndCleanTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-cliscan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Scan_Returns_Success_When_Target_Discovered()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "file.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 200)
        );
        fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var args = new CliArgs(
            "scan",
            null,
            false,
            false,
            false,
            "ndm",
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

        var exitCode = ScanHandler.Execute(args, fakeRegistry);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }

    [Test]
    public void Scan_Fails_When_App_Not_Found()
    {
        var fakeRegistry = new FakeRegistryAccessor(); // no registry entries
        var args = new CliArgs(
            "scan",
            null,
            false,
            false,
            false,
            "ndm",
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

        var exitCode = ScanHandler.Execute(args, fakeRegistry);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.TargetAppNotFoundOrPathInvalid));
    }

    [Test]
    public void Clean_Sweeps_Orphans_And_Returns_Success()
    {
        var workDir = Path.Combine(_testDir, "work");
        var tmpDir = Path.Combine(workDir, ".mdma-tmp");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllText(Path.Combine(tmpDir, "leftover.mdma"), "garbage");

        var args = new CliArgs(
            "clean",
            workDir,
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

        var exitCode = CleanHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(Directory.EnumerateFileSystemEntries(tmpDir), Is.Empty);
    }

    [Test]
    public void Clean_Returns_Success_When_No_Orphans_Present()
    {
        var args = new CliArgs(
            "clean",
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

        var exitCode = CleanHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }
}
