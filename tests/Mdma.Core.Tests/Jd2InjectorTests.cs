using System.IO.Compression;
using System.Text.Json;
using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class Jd2InjectorTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-jd2injector-test-" + Guid.NewGuid());
        var workRootPath = Path.Combine(_testDir, "workroot");
        Directory.CreateDirectory(workRootPath);
        _workingRoot = new WorkingRoot(workRootPath, true, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private MdmaPackage BuildLoadedPackage(string filename, string url, long totalBytes, (long start, long end, byte[] bytes)[] chunks)
    {
        var stagingDir = Path.Combine(_testDir, "export-staging-" + Guid.NewGuid());
        Directory.CreateDirectory(stagingDir);

        var sources = new List<MdmaChunkSource>();
        for (int i = 0; i < chunks.Length; i++)
        {
            var path = Path.Combine(stagingDir, $"src_{i}.bin");
            File.WriteAllBytes(path, chunks[i].bytes);
            sources.Add(new MdmaChunkSource(i, chunks[i].start, chunks[i].end, path));
        }

        var mdmaPath = Path.Combine(_testDir, $"pkg-{Guid.NewGuid():N}.mdma");
        new MdmaPackageWriter().WritePackage(TargetApp.NDM, url, filename, totalBytes, null,
            Array.Empty<KeyValuePair<string, string>>(), 1785268000000L, sources, mdmaPath);

        return new MdmaLoader().Load(mdmaPath, _workingRoot).Value!;
    }

    [Test]
    public void Inject_Reconstructs_PartFile_With_Correct_Bytes_At_Correct_Offsets()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);

        var chunk0 = new byte[] { 1, 2, 3, 4, 5 };
        var chunk1 = new byte[] { 6, 7, 8, 9, 10 };
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 10, new[] { (0L, 4L, chunk0), (5L, 9L, chunk1) });

        var location = new TargetAppLocation(TargetApp.JD2, cfgDir, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.True);
        var partBytes = File.ReadAllBytes(Path.Combine(downloadFolder, "f.bin.part"));
        Assert.That(partBytes, Has.Length.EqualTo(10));
        Assert.That(partBytes[0..5], Is.EqualTo(chunk0));
        Assert.That(partBytes[5..10], Is.EqualTo(chunk1));
    }

    [Test]
    public void Inject_Creates_DownloadList1_When_No_Existing_Zips()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);

        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 5, new[] { (0L, 4L, new byte[5]) });
        var location = new TargetAppLocation(TargetApp.JD2, cfgDir, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        injector.Inject(package, location);

        Assert.That(File.Exists(Path.Combine(cfgDir, "downloadList1.zip")), Is.True);
    }

    [Test]
    public void Inject_Increments_Counter_Past_Existing_Newest_Zip()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var jd2Fixture = new Jd2FixtureBuilder(_testDir, counter: 5)
            .WithLink("00", "00", "existing.bin", "https://example.com/existing.bin", 100, 100, 100);
        jd2Fixture.Build();

        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 5, new[] { (0L, 4L, new byte[5]) });
        var location = new TargetAppLocation(TargetApp.JD2, jd2Fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        injector.Inject(package, location);

        Assert.That(File.Exists(Path.Combine(jd2Fixture.CfgDirectory, "downloadList6.zip")), Is.True);
        Assert.That(File.Exists(Path.Combine(jd2Fixture.CfgDirectory, "downloadList5.zip")), Is.True, "old zip must not be deleted");
    }

    [Test]
    public void Inject_Preserves_Existing_Entries_In_New_Zip()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var jd2Fixture = new Jd2FixtureBuilder(_testDir, counter: 1)
            .WithLink("00", "00", "existing.bin", "https://example.com/existing.bin", 100, 100, 100);
        jd2Fixture.Build();

        var package = BuildLoadedPackage("new.bin", "https://example.com/new.bin", 5, new[] { (0L, 4L, new byte[5]) });
        var location = new TargetAppLocation(TargetApp.JD2, jd2Fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        injector.Inject(package, location);

        using var zip = ZipFile.OpenRead(Path.Combine(jd2Fixture.CfgDirectory, "downloadList2.zip"));
        Assert.That(zip.GetEntry("00_00"), Is.Not.Null, "the pre-existing link entry must survive into the new zip");
        Assert.That(zip.GetEntry("00"), Is.Not.Null, "the pre-existing package entry must survive into the new zip");
    }

    [Test]
    public void Inject_Assigns_New_Package_Id_Not_Colliding_With_Existing()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var jd2Fixture = new Jd2FixtureBuilder(_testDir, counter: 1)
            .WithLink("00", "00", "a.bin", "https://example.com/a.bin", 100, 100, 100)
            .WithLink("05", "00", "b.bin", "https://example.com/b.bin", 100, 100, 100);
        jd2Fixture.Build();

        var package = BuildLoadedPackage("new.bin", "https://example.com/new.bin", 5, new[] { (0L, 4L, new byte[5]) });
        var location = new TargetAppLocation(TargetApp.JD2, jd2Fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        injector.Inject(package, location);

        using var zip = ZipFile.OpenRead(Path.Combine(jd2Fixture.CfgDirectory, "downloadList2.zip"));
        Assert.That(zip.GetEntry("6"), Is.Not.Null, "new package id should be max existing (5) + 1 = 6");
        Assert.That(zip.GetEntry("6_00"), Is.Not.Null);
    }

    [Test]
    public void Inject_New_Link_Entry_Has_Correct_Size_And_Current()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);

        // 20 bytes total, only 15 downloaded
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 20,
            new[] { (0L, 9L, new byte[10]), (10L, 19L, new byte[5]) });
        var location = new TargetAppLocation(TargetApp.JD2, cfgDir, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        injector.Inject(package, location);

        using var zip = ZipFile.OpenRead(Path.Combine(cfgDir, "downloadList1.zip"));
        var linkEntry = zip.Entries.Single(e => e.Name.Contains('_'));
        using var stream = linkEntry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("size").GetInt64(), Is.EqualTo(20));
        Assert.That(root.GetProperty("current").GetInt64(), Is.EqualTo(15));
        Assert.That(root.GetProperty("properties").GetProperty("CHUNKS").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public void Inject_Fails_Cleanly_When_InstallOrConfigDir_Missing()
    {
        var location = new TargetAppLocation(TargetApp.JD2, InstallOrConfigDir: null, MetadataDir: null, DownloadDirectory: _testDir, WasAutoDetected: false);
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 5, new[] { (0L, 4L, new byte[5]) });

        var injector = new Jd2Injector(new AtomicWriter());
        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }

    [Test]
    public void Inject_Fails_Cleanly_When_DownloadDirectory_Missing()
    {
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var location = new TargetAppLocation(TargetApp.JD2, cfgDir, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: false);
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 5, new[] { (0L, 4L, new byte[5]) });

        var injector = new Jd2Injector(new AtomicWriter());
        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }

    [Test]
    public void Inject_Two_Tasks_Sequentially_Both_Succeed_With_Distinct_Ids()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var location = new TargetAppLocation(TargetApp.JD2, cfgDir, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var injector = new Jd2Injector(new AtomicWriter());

        var package1 = BuildLoadedPackage("a.bin", "https://example.com/a.bin", 5, new[] { (0L, 4L, new byte[5]) });
        var result1 = injector.Inject(package1, location);

        var package2 = BuildLoadedPackage("b.bin", "https://example.com/b.bin", 5, new[] { (0L, 4L, new byte[5]) });
        var result2 = injector.Inject(package2, location);

        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(File.Exists(Path.Combine(cfgDir, "downloadList1.zip")), Is.True);
        Assert.That(File.Exists(Path.Combine(cfgDir, "downloadList2.zip")), Is.True);

        using var zip2 = ZipFile.OpenRead(Path.Combine(cfgDir, "downloadList2.zip"));
        // both the original package from downloadList1 and the newly-added one should be present
        var packageEntries = zip2.Entries.Where(e => e.Name != "extraInfo" && !e.Name.Contains('_')).ToList();
        Assert.That(packageEntries, Has.Count.EqualTo(2));
    }
}
