using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class Jd2ListReaderTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-jd2listreader-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void ScanTasks_Returns_Correct_Summary_For_Single_Link()
    {
        var fixture = new Jd2FixtureBuilder(_testDir).WithLink(
            "99",
            "00",
            "poc_test_file.bin",
            "https://speed.hetzner.de/100MB.bin",
            10_485_760,
            2_097_152,
            2_097_152
        );
        fixture.Build();

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        var task = result.Value![0];
        Assert.That(task.NativeId, Is.EqualTo("99_00"));
        Assert.That(task.Filename, Is.EqualTo("poc_test_file.bin"));
        Assert.That(task.Url, Is.EqualTo("https://speed.hetzner.de/100MB.bin"));
        Assert.That(task.TotalBytes, Is.EqualTo(10_485_760));
        Assert.That(task.DownloadedBytes, Is.EqualTo(2_097_152));
        Assert.That(task.Resumable, Is.True);
        Assert.That(task.StatusText, Is.EqualTo("Paused ( 20% )"));
    }

    [Test]
    public void ScanTasks_Flattens_Multiple_Packages_And_Links()
    {
        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithLink("00", "00", "a.bin", "https://example.com/a.bin", 1000, 500)
            .WithLink("00", "01", "b.bin", "https://example.com/b.bin", 2000, 2000)
            .WithLink("01", "00", "c.bin", "https://example.com/c.bin", 3000, 0);
        fixture.Build();

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.Value, Has.Count.EqualTo(3));
        Assert.That(
            result.Value!.Select(t => t.NativeId),
            Is.EquivalentTo(new[] { "00_00", "00_01", "01_00" })
        );
    }

    [Test]
    public void ScanTasks_Picks_Newest_Zip_When_Stale_Duplicate_Present()
    {
        var fixture = new Jd2FixtureBuilder(_testDir, counter: 5).WithLink(
            "99",
            "00",
            "newest_file.bin",
            "https://example.com/newest.bin",
            1000,
            100
        );
        fixture.Build();
        fixture.BuildStaleDuplicate(1); // older, valid but distinguishable by filename below

        // Overwrite the stale one's link content so we can prove it's ignored.
        var staleBuilder = new Jd2FixtureBuilder(_testDir, counter: 1).WithLink(
            "99",
            "00",
            "stale_file.bin",
            "https://example.com/stale.bin",
            1000,
            999
        );
        staleBuilder.Build();

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Filename, Is.EqualTo("newest_file.bin"));
    }

    [Test]
    public void ScanTasks_Fails_Cleanly_When_InstallOrConfigDir_Missing_On_Location()
    {
        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            InstallOrConfigDir: null,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ScanFailed));
    }

    [Test]
    public void ScanTasks_Fails_Cleanly_When_No_Zip_Files_Present()
    {
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            cfgDir,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ScanFailed));
    }

    [Test]
    public void ScanTasks_Returns_Empty_List_For_Package_With_No_Links()
    {
        // Build a zip with only a package entry and extraInfo, no link entries.
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var zipPath = Path.Combine(cfgDir, "downloadList1.zip");
        using (
            var zip = System.IO.Compression.ZipFile.Open(
                zipPath,
                System.IO.Compression.ZipArchiveMode.Create
            )
        )
        {
            var pkgEntry = zip.CreateEntry("99");
            using (var w = new StreamWriter(pkgEntry.Open()))
                w.Write(
                    """{"uid":1,"name":"Empty Package","downloadFolder":"D:\\Downloads","created":1,"enabled":true}"""
                );

            var infoEntry = zip.CreateEntry("extraInfo");
            using (var w = new StreamWriter(infoEntry.Open()))
                w.Write("""{"version":2}""");
        }

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            cfgDir,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public void ScanTasks_Handles_Fully_Downloaded_Task_As_100_Percent()
    {
        var fixture = new Jd2FixtureBuilder(_testDir).WithLink(
            "99",
            "00",
            "complete.bin",
            "https://example.com/complete.bin",
            5000,
            5000
        );
        fixture.Build();

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.Value![0].PercentComplete, Is.EqualTo(100.0).Within(0.01));
    }

    [Test]
    public void ScanTasks_Resumable_False_When_Property_Missing_Or_False()
    {
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var zipPath = Path.Combine(cfgDir, "downloadList1.zip");
        using (
            var zip = System.IO.Compression.ZipFile.Open(
                zipPath,
                System.IO.Compression.ZipArchiveMode.Create
            )
        )
        {
            var linkEntry = zip.CreateEntry("99_00");
            using var w = new StreamWriter(linkEntry.Open());
            w.Write(
                """{"name":"f.bin","url":"https://example.com/f.bin","size":100,"current":50}"""
            ); // no "properties" at all
        }

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            cfgDir,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.Value![0].Resumable, Is.False);
    }

    [Test]
    public void ScanTasks_Handles_Link_With_Null_Properties_Field_Without_Throwing()
    {
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var zipPath = Path.Combine(cfgDir, "downloadList1.zip");
        using (
            var zip = System.IO.Compression.ZipFile.Open(
                zipPath,
                System.IO.Compression.ZipArchiveMode.Create
            )
        )
        {
            var linkEntry = zip.CreateEntry("99_00");
            using var w = new StreamWriter(linkEntry.Open());
            w.Write(
                """{"name":"f.bin","url":"https://example.com/f.bin","size":100,"current":50,"properties":null}"""
            );
        }

        var reader = new Jd2ListReader();
        var location = new TargetAppLocation(
            TargetApp.JD2,
            cfgDir,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value![0].Filename, Is.EqualTo("f.bin"));
        Assert.That(result.Value[0].Resumable, Is.False);
    }
}
