using Mdma.Cli.Handlers;
using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using NUnit.Framework;

namespace Mdma.Cli.Tests;

public class ConversionHandlersTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-cliconvert-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Export_Succeeds_For_Valid_Task_Via_AutoDetect()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var outPath = Path.Combine(_testDir, "out.mdma");
        // ManualPath set to null so locator uses auto-detect against fakeRegistry
        var args = new CliArgs(
            "export",
            _testDir,
            false,
            false,
            false,
            "ndm",
            null,
            null,
            "521",
            outPath,
            null,
            null,
            null,
            _testDir,
            null
        );

        var exitCode = ExportHandler.Execute(args, fakeRegistry);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(File.Exists(outPath), Is.True);
    }

    [Test]
    public void Export_Succeeds_With_ManualPath_And_TempDir_Override()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var outPath = Path.Combine(_testDir, "out_manual.mdma");
        // ManualPath set to _testDir, TempDir set to fixture.TempDirectory
        var args = new CliArgs(
            "export",
            _testDir,
            false,
            false,
            false,
            "ndm",
            null,
            null,
            "521",
            outPath,
            null,
            _testDir,
            fixture.TempDirectory,
            null,
            null
        );

        var exitCode = ExportHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(File.Exists(outPath), Is.True);
    }

    [Test]
    public void Export_Fails_When_Required_Arguments_Missing()
    {
        var args = new CliArgs(
            "export",
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

        var exitCode = ExportHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.TargetAppNotFoundOrPathInvalid));
    }

    [Test]
    public void Export_Fails_When_Task_Id_Not_Found()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var args = new CliArgs(
            "export",
            _testDir,
            false,
            false,
            false,
            "ndm",
            null,
            null,
            "999",
            Path.Combine(_testDir, "out.mdma"),
            null,
            null,
            null,
            _testDir,
            null
        );

        var exitCode = ExportHandler.Execute(args, fakeRegistry);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.OperationFailed));
    }

    [Test]
    public void Import_Fails_When_File_Not_Found()
    {
        var args = new CliArgs(
            "import",
            _testDir,
            false,
            false,
            false,
            "ndm",
            null,
            null,
            null,
            null,
            Path.Combine(_testDir, "nope.mdma"),
            null,
            null,
            null,
            null
        );

        var exitCode = ImportHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.PackageOrChecksumError));
    }

    [Test]
    public void Import_Succeeds_For_Valid_Package()
    {
        var ndmFixture = new NdmFixtureBuilder(_testDir);
        ndmFixture.Build();

        var mdmaPath = new MdmaFixtureBuilder()
            .WithTotalBytes(100)
            .WithChunk(0, 0, 99, new byte[100])
            .BuildValid(Path.Combine(_testDir, "test.mdma"));

        var args = new CliArgs(
            "import",
            _testDir,
            false,
            false,
            false,
            "ndm",
            null,
            null,
            null,
            null,
            mdmaPath,
            _testDir,
            ndmFixture.TempDirectory,
            _testDir,
            null
        );

        var exitCode = ImportHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }

    [Test]
    public void Convert_Fails_When_Required_Flags_Missing()
    {
        var args = new CliArgs(
            "convert",
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

        var exitCode = ConvertHandler.Execute(args);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.TargetAppNotFoundOrPathInvalid));
    }

    [Test]
    public void Convert_Succeeds_SameMachine_Ndm_To_Jd2()
    {
        var sourceRoot = Path.Combine(_testDir, "ndm-source");
        var ndmFixture = new NdmFixtureBuilder(sourceRoot).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            10,
            (0, 9, 10)
        );
        ndmFixture.Build();

        var destCfgDir = Path.Combine(_testDir, "jd2-dest", "cfg");
        var destDownloadFolder = Path.Combine(_testDir, "jd2-dest", "downloads");
        var jd2Fixture = new Jd2FixtureBuilder(Path.Combine(_testDir, "jd2-dest"))
            .WithDefaultDownloadFolder(destDownloadFolder)
            .WithLink("99", "00", "existing.bin", "https://example.com/existing.bin", 10, 10, 10);
        jd2Fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", ndmFixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", ndmFixture.DownloadDirectory);

        var args = new CliArgs(
            "convert",
            _testDir,
            false,
            false,
            false,
            null,
            "ndm",
            "jd2",
            "1",
            null,
            null,
            null,
            null,
            sourceRoot,
            null
        );

        // Point JD2 auto-detect to fake appdata location
        var locator = new Jd2Locator(Path.Combine(_testDir, "jd2-dest"));

        var exitCode = ConvertHandler.Execute(args, fakeRegistry);

        Assert.That(exitCode, Is.EqualTo(ExitCodes.Success));
    }
}
