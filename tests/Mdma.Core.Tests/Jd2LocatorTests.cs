using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class Jd2LocatorTests
{
    private string _testDir = null!;
    private string _fakeAppDataDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-jd2locator-test-" + Guid.NewGuid());
        _fakeAppDataDir = Path.Combine(_testDir, "appdata");
        Directory.CreateDirectory(_fakeAppDataDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void AutoDetect_Succeeds_When_Default_CfgDir_Has_Valid_Zip()
    {
        var jd2Root = Path.Combine(_fakeAppDataDir, "JDownloader 2");
        var fixture = new Jd2FixtureBuilder(jd2Root)
            .WithDefaultDownloadFolder(@"D:\Downloads")
            .WithLink("99", "00", "file.bin", "https://example.com/f.bin", 1000, 200, 200);
        fixture.Build();

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.InstallOrConfigDir, Is.EqualTo(fixture.CfgDirectory));
        Assert.That(result.Value.WasAutoDetected, Is.True);
        Assert.That(result.Value.MetadataDir, Is.Null);
        Assert.That(result.Value.DownloadDirectory, Is.EqualTo(@"D:\Downloads"));
    }

    [Test]
    public void AutoDetect_Succeeds_With_Null_DownloadDirectory_When_Settings_File_Missing()
    {
        var jd2Root = Path.Combine(_fakeAppDataDir, "JDownloader 2");
        var fixture = new Jd2FixtureBuilder(
            jd2Root
        ) // no WithDefaultDownloadFolder call
        .WithLink("99", "00", "file.bin", "https://example.com/f.bin", 1000, 200, 200);
        fixture.Build();

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.DownloadDirectory, Is.Null);
    }

    [Test]
    public void AutoDetect_Fails_Cleanly_When_CfgDir_Does_Not_Exist()
    {
        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppNotFound));
    }

    [Test]
    public void AutoDetect_Fails_Cleanly_When_CfgDir_Exists_But_Has_No_Zips()
    {
        var jd2Root = Path.Combine(_fakeAppDataDir, "JDownloader 2");
        Directory.CreateDirectory(Path.Combine(jd2Root, "cfg"));

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Succeeds_Against_Valid_Fixture()
    {
        var fixture = new Jd2FixtureBuilder(_testDir).WithLink(
            "99",
            "00",
            "file.bin",
            "https://example.com/f.bin",
            1000,
            200,
            200
        );
        fixture.Build();

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.ValidateManualPath(fixture.CfgDirectory);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WasAutoDetected, Is.False);
    }

    [Test]
    public void ValidateManualPath_Fails_When_Directory_Does_Not_Exist()
    {
        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.ValidateManualPath(Path.Combine(_testDir, "nope"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Fails_When_Zip_Has_No_Expected_Entries()
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
            var entry = zip.CreateEntry("not_a_valid_entry_name.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("irrelevant");
        }

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.ValidateManualPath(cfgDir);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Fails_When_Zip_Is_Corrupt()
    {
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "downloadList1.zip"), "not actually a zip file");

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.ValidateManualPath(cfgDir);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void PickNewest_Selects_Highest_Numbered_File_Regardless_Of_Order()
    {
        var paths = new[]
        {
            @"C:\cfg\downloadList100.zip",
            @"C:\cfg\downloadList99999.zip",
            @"C:\cfg\downloadList2.zip",
        };

        var newest = Jd2Locator.PickNewest(paths);

        Assert.That(newest, Is.EqualTo(@"C:\cfg\downloadList99999.zip"));
    }

    [Test]
    public void ValidateManualPath_Uses_Newest_Zip_When_Stale_Duplicate_Present()
    {
        var fixture = new Jd2FixtureBuilder(_testDir, counter: 5).WithLink(
            "99",
            "00",
            "file.bin",
            "https://example.com/f.bin",
            1000,
            200,
            200
        );
        fixture.Build();
        // Add a stale, deliberately-invalid older zip alongside the valid newest one.
        var staleZip = Path.Combine(fixture.CfgDirectory, "downloadList1.zip");
        File.WriteAllText(staleZip, "corrupt stale file, should be ignored");

        var locator = new Jd2Locator(_fakeAppDataDir);
        var result = locator.ValidateManualPath(fixture.CfgDirectory);

        Assert.That(
            result.IsSuccess,
            Is.True,
            "validation should succeed based on the newest (valid) zip, ignoring the stale corrupt one"
        );
    }
}
