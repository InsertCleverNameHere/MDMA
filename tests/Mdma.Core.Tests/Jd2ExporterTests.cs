using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class Jd2ExporterTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-jd2exporter-test-" + Guid.NewGuid());
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

    private static DownloadTaskSummary MakeSummary(string nativeId, string filename, string url, long total, long downloaded) =>
        new(nativeId, TargetApp.JD2, filename, url, total, downloaded, $"Paused ( {(int)(downloaded * 100 / total)}% )", true);

    [Test]
    public void Export_SingleChunk_Produces_Valid_Mdma_With_Correct_Bytes()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        var fileBytes = new byte[10];
        new Random(1).NextBytes(fileBytes);
        File.WriteAllBytes(Path.Combine(downloadFolder, "f.bin.part"), fileBytes);

        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithPackageDownloadFolder("99", downloadFolder)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 10, 10, 10); // 1 chunk, fully downloaded
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 10, 10);

        var exporter = new Jd2Exporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        var exportResult = exporter.Export(task, location, _workingRoot, destPath);

        Assert.That(exportResult.IsSuccess, Is.True);

        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        Assert.That(loadResult.IsSuccess, Is.True);
        Assert.That(loadResult.Value!.Manifest.Origin, Is.EqualTo(TargetApp.JD2));
        Assert.That(loadResult.Value.Manifest.Chunks, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllBytes(loadResult.Value.ChunkFilePaths[0]), Is.EqualTo(fileBytes));
    }

    [Test]
    public void Export_MultiChunk_Slices_Correct_Byte_Ranges()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        // 20-byte file, 2 chunks of 10 bytes each, both fully downloaded.
        var fileBytes = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(Path.Combine(downloadFolder, "f.bin.part"), fileBytes);

        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithPackageDownloadFolder("99", downloadFolder)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 20, 20, 10, 20); // chunkProgress: [10, 20] absolute offsets
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 20, 20);

        var exporter = new Jd2Exporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        exporter.Export(task, location, _workingRoot, destPath);

        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        Assert.That(loadResult.Value!.Manifest.Chunks, Has.Count.EqualTo(2));

        var chunk0Bytes = File.ReadAllBytes(loadResult.Value.ChunkFilePaths[0]);
        var chunk1Bytes = File.ReadAllBytes(loadResult.Value.ChunkFilePaths[1]);

        Assert.That(chunk0Bytes, Is.EqualTo(fileBytes[0..10]));
        Assert.That(chunk1Bytes, Is.EqualTo(fileBytes[10..20]));
    }

    [Test]
    public void Export_MultiChunk_Handles_Partial_Progress_Per_Chunk()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        var fileBytes = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(Path.Combine(downloadFolder, "f.bin.part"), fileBytes);

        // chunk 0 fully done (progress=10), chunk 1 only half done (progress=15, chunk starts at 10)
        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithPackageDownloadFolder("99", downloadFolder)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 20, 15, 10, 15);
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 20, 15);

        var exporter = new Jd2Exporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        exporter.Export(task, location, _workingRoot, destPath);

        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        var chunk1Bytes = File.ReadAllBytes(loadResult.Value!.ChunkFilePaths[1]);

        Assert.That(chunk1Bytes, Has.Length.EqualTo(5)); // only 5 of 10 bytes downloaded in chunk 1
        Assert.That(chunk1Bytes, Is.EqualTo(fileBytes[10..15]));
    }

    [Test]
    public void Export_Uses_Completed_File_When_Part_File_Absent()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        var fileBytes = new byte[10];
        new Random(2).NextBytes(fileBytes);
        File.WriteAllBytes(Path.Combine(downloadFolder, "f.bin"), fileBytes); // no ".part" suffix -- completed download

        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithPackageDownloadFolder("99", downloadFolder)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 10, 10, 10);
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 10, 10);

        var exporter = new Jd2Exporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        var result = exporter.Export(task, location, _workingRoot, destPath);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Export_PackageDownloadFolder_Takes_Priority_Over_AppLevel_Fallback()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        var fileBytes = new byte[5];
        File.WriteAllBytes(Path.Combine(downloadFolder, "f.bin.part"), fileBytes);

        // Package's downloadFolder is NOT set via WithPackageDownloadFolder, so
        // it falls back to the fixture's placeholder "D:\Downloads", which
        // won't exist on the test machine. location.DownloadDirectory (the
        // app-level fallback) IS a real, valid folder -- but per the exporter's
        // documented priority, the (non-existent) package-level folder should
        // still be tried first, causing this export to fail.
        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 5, 5, 5);
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new Jd2Exporter();
        var result = exporter.Export(task, location, _workingRoot, Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False,
            "package-level downloadFolder should take priority over the app-level fallback when present, even if it doesn't resolve to a real path in this test");
    }

    [Test]
    public void Export_Uses_AppLevel_Fallback_When_Package_Entry_Itself_Is_Missing()
    {
        // Build a zip with ONLY a link entry, no matching package entry at all --
        // this is the actual scenario where the app-level DownloadDirectory
        // fallback should kick in and succeed.
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        var fileBytes = new byte[5];
        File.WriteAllBytes(Path.Combine(downloadFolder, "f.bin.part"), fileBytes);

        var cfgDir = Path.Combine(_testDir, "cfg");
        Directory.CreateDirectory(cfgDir);
        var zipPath = Path.Combine(cfgDir, "downloadList1.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var linkEntry = zip.CreateEntry("99_00");
            using var w = new StreamWriter(linkEntry.Open());
            w.Write("""{"name":"f.bin","url":"https://example.com/f.bin","size":5,"current":5,"chunkProgress":[5],"properties":{"CHUNKS":1}}""");
            // deliberately no "99" package entry
        }

        var location = new TargetAppLocation(TargetApp.JD2, cfgDir, MetadataDir: null, DownloadDirectory: downloadFolder, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new Jd2Exporter();
        var result = exporter.Export(task, location, _workingRoot, Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Export_Fails_Cleanly_When_InstallOrConfigDir_Missing()
    {
        var location = new TargetAppLocation(TargetApp.JD2, InstallOrConfigDir: null, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: false);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new Jd2Exporter();
        var result = exporter.Export(task, location, _workingRoot, Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void Export_Fails_Cleanly_When_NativeId_Format_Invalid()
    {
        var location = new TargetAppLocation(TargetApp.JD2, _testDir, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: false);
        var task = MakeSummary("not-a-valid-native-id", "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new Jd2Exporter();
        var result = exporter.Export(task, location, _workingRoot, Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void Export_Fails_Cleanly_When_No_Source_File_Found()
    {
        var downloadFolder = Path.Combine(_testDir, "downloads");
        Directory.CreateDirectory(downloadFolder);
        // No .part or completed file written.

        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithPackageDownloadFolder("99", downloadFolder)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 10, 5, 5);
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);
        var task = MakeSummary("99_00", "f.bin", "https://example.com/f.bin", 10, 5);

        var exporter = new Jd2Exporter();
        var result = exporter.Export(task, location, _workingRoot, Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void Export_Fails_Cleanly_When_Link_Entry_Not_Found()
    {
        var fixture = new Jd2FixtureBuilder(_testDir)
            .WithLink("99", "00", "f.bin", "https://example.com/f.bin", 10, 5, 5);
        fixture.Build();

        var location = new TargetAppLocation(TargetApp.JD2, fixture.CfgDirectory, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);
        var task = MakeSummary("99_99", "f.bin", "https://example.com/f.bin", 10, 5); // wrong link index

        var exporter = new Jd2Exporter();
        var result = exporter.Export(task, location, _workingRoot, Path.Combine(_testDir, "out.mdma"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }
}
