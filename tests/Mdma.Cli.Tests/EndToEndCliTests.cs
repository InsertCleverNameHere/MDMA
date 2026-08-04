using Mdma.Core.Tests.Fixtures;
using NUnit.Framework;

namespace Mdma.Cli.Tests;

public class EndToEndCliTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-clie2e-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Full_Lifecycle_Scan_Export_Import_Backups_Revert_Clean_Succeeds()
    {
        var sourceDir = Path.Combine(_testDir, "source");
        var destMetaDir = Path.Combine(_testDir, "dest-meta");
        var destTempDir = Path.Combine(_testDir, "dest-temp");
        var workDir = Path.Combine(_testDir, "work");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destMetaDir);
        Directory.CreateDirectory(destTempDir);
        Directory.CreateDirectory(workDir);

        // 1. Setup source NDM task
        var fixture = new NdmFixtureBuilder(sourceDir).WithTask(
            521,
            "lifecycle.bin",
            "https://example.com/file.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var mdmaOutPath = Path.Combine(_testDir, "exported.mdma");

        // 2. Scan
        var scanExit = Program.Main(
            new[] { "scan", "--app", "ndm", "--path", sourceDir, "--workdir", workDir, "--json" }
        );
        Assert.That(scanExit, Is.EqualTo(ExitCodes.Success));

        // 3. Export
        var exportExit = Program.Main(
            new[]
            {
                "export",
                "--app",
                "ndm",
                "--id",
                "521",
                "--out",
                mdmaOutPath,
                "--path",
                sourceDir,
                "--temp-dir",
                fixture.TempDirectory,
                "--workdir",
                workDir,
                "--json",
            }
        );
        Assert.That(exportExit, Is.EqualTo(ExitCodes.Success));
        Assert.That(File.Exists(mdmaOutPath), Is.True);

        // 4. Import
        var importExit = Program.Main(
            new[]
            {
                "import",
                "--app",
                "ndm",
                "--file",
                mdmaOutPath,
                "--metadata-dir",
                destMetaDir,
                "--temp-dir",
                destTempDir,
                "--workdir",
                workDir,
                "--json",
            }
        );
        Assert.That(importExit, Is.EqualTo(ExitCodes.Success));

        // 5. Backups
        var backupsExit = Program.Main(new[] { "backups", "--workdir", workDir, "--json" });
        Assert.That(backupsExit, Is.EqualTo(ExitCodes.Success));

        // 6. Clean
        var cleanExit = Program.Main(new[] { "clean", "--workdir", workDir, "--json" });
        Assert.That(cleanExit, Is.EqualTo(ExitCodes.Success));
    }

    [Test]
    public void Full_Lifecycle_Convert_SameMachine_Succeeds()
    {
        var sourceDir = Path.Combine(_testDir, "source");
        var destCfgDir = Path.Combine(_testDir, "jd2-dest", "cfg");
        var destDownloadFolder = Path.Combine(_testDir, "jd2-dest", "downloads");
        var workDir = Path.Combine(_testDir, "work");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destCfgDir);
        Directory.CreateDirectory(workDir);

        var ndmFixture = new NdmFixtureBuilder(sourceDir).WithTask(
            1,
            "convert.bin",
            "https://example.com/convert.bin",
            10,
            (0, 9, 10)
        );
        ndmFixture.Build();

        var jd2Fixture = new Jd2FixtureBuilder(Path.Combine(_testDir, "jd2-dest"))
            .WithDefaultDownloadFolder(destDownloadFolder)
            .WithLink("99", "00", "existing.bin", "https://example.com/existing.bin", 10, 10, 10);
        jd2Fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", ndmFixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", ndmFixture.DownloadDirectory);

        var convertExit = Program.Main(
            new[]
            {
                "convert",
                "--source",
                "ndm",
                "--dest",
                "jd2",
                "--id",
                "1",
                "--path",
                sourceDir,
                "--temp-dir",
                ndmFixture.TempDirectory,
                "--download-dir",
                destDownloadFolder,
                "--workdir",
                workDir,
                "--json",
            }
        );

        Assert.That(convertExit, Is.EqualTo(ExitCodes.Success));
    }

    [Test]
    public void Program_Main_Returns_NonZero_ExitCode_On_Invalid_Path()
    {
        var exitCode = Program.Main(
            new[]
            {
                "import",
                "--app",
                "ndm",
                "--file",
                Path.Combine(_testDir, "nope.mdma"),
                "--json",
            }
        );

        Assert.That(exitCode, Is.EqualTo(ExitCodes.PackageOrChecksumError));
    }
}
