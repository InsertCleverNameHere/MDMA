using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class NdmExporterTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-ndmexporter-test-" + Guid.NewGuid());
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

    private static DownloadTaskSummary MakeSummary(
        int taskId,
        string filename,
        string url,
        long total,
        long downloaded
    ) =>
        new(
            taskId.ToString(),
            TargetApp.NDM,
            filename,
            url,
            total,
            downloaded,
            $"Paused ( {(int)(downloaded * 100 / total)}% )",
            true
        );

    [Test]
    public void Export_Produces_Valid_Mdma_That_Loads_Back_Correctly()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "poc.bin",
            "https://example.com/f.bin",
            10,
            (0, 4, 5),
            (5, 9, 5)
        );
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var task = MakeSummary(521, "poc.bin", "https://example.com/f.bin", 10, 10);

        var exporter = new NdmExporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        var exportResult = exporter.Export(task, location, _workingRoot, destPath);

        Assert.That(exportResult.IsSuccess, Is.True);
        Assert.That(File.Exists(destPath), Is.True);

        var loader = new MdmaLoader();
        var loadResult = loader.Load(destPath, _workingRoot);

        Assert.That(loadResult.IsSuccess, Is.True);
        Assert.That(loadResult.Value!.Manifest.Origin, Is.EqualTo(TargetApp.NDM));
        Assert.That(loadResult.Value.Manifest.Url, Is.EqualTo("https://example.com/f.bin"));
        Assert.That(loadResult.Value.Manifest.Chunks, Has.Count.EqualTo(2));
        Assert.That(loadResult.Value.ChunkFilePaths, Has.Count.EqualTo(2));
    }

    [Test]
    public void Export_Packaged_Chunk_Bytes_Match_Original_Segment_Files()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            5,
            (0, 4, 5)
        );
        fixture.Build();
        var originalSegBytes = File.ReadAllBytes(
            Path.Combine(fixture.TempDirectory, "1", "seg.x0")
        );

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var task = MakeSummary(1, "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new NdmExporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        exporter.Export(task, location, _workingRoot, destPath);

        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        var stagedBytes = File.ReadAllBytes(loadResult.Value!.ChunkFilePaths[0]);

        Assert.That(stagedBytes, Is.EqualTo(originalSegBytes));
    }

    [Test]
    public void Export_Includes_MimeType_And_Headers_From_Db()
    {
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(1, "f.bin", "https://example.com/f.bin", 5, (0, 4, 5))
            .WithMetadata(
                1,
                mimeType: "application/octet-stream",
                ("Referer", "https://example.com"),
                ("Cookie", "session=abc")
            );
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var task = MakeSummary(1, "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new NdmExporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        exporter.Export(task, location, _workingRoot, destPath);

        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        var manifest = loadResult.Value!.Manifest;

        Assert.That(manifest.MimeType, Is.EqualTo("application/octet-stream"));
        Assert.That(manifest.Headers, Has.Count.EqualTo(2));
        Assert.That(
            manifest.Headers,
            Does.Contain(new KeyValuePair<string, string>("Referer", "https://example.com"))
        );
        Assert.That(
            manifest.Headers,
            Does.Contain(new KeyValuePair<string, string>("Cookie", "session=abc"))
        );
    }

    [Test]
    public void Export_Handles_Task_With_No_Metadata_Gracefully()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            5,
            (0, 4, 5)
        ); // no WithMetadata call
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var task = MakeSummary(1, "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new NdmExporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        var result = exporter.Export(task, location, _workingRoot, destPath);

        Assert.That(result.IsSuccess, Is.True);
        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        Assert.That(loadResult.Value!.Manifest.MimeType, Is.Null);
        Assert.That(loadResult.Value.Manifest.Headers, Is.Empty);
    }

    [Test]
    public void Export_Preserves_Chunk_Byte_Ranges_From_SegmentsBin()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            20,
            (0, 9, 10),
            (10, 19, 10)
        );
        fixture.Build();

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var task = MakeSummary(1, "f.bin", "https://example.com/f.bin", 20, 20);

        var exporter = new NdmExporter();
        var destPath = Path.Combine(_testDir, "out.mdma");
        exporter.Export(task, location, _workingRoot, destPath);

        var loadResult = new MdmaLoader().Load(destPath, _workingRoot);
        var chunks = loadResult.Value!.Manifest.Chunks.OrderBy(c => c.Index).ToList();

        Assert.That(chunks[0].StartByte, Is.EqualTo(0));
        Assert.That(chunks[0].EndByte, Is.EqualTo(9));
        Assert.That(chunks[1].StartByte, Is.EqualTo(10));
        Assert.That(chunks[1].EndByte, Is.EqualTo(19));
    }

    [Test]
    public void Export_Fails_Cleanly_When_InstallOrConfigDir_Missing()
    {
        var location = new TargetAppLocation(
            TargetApp.NDM,
            InstallOrConfigDir: null,
            MetadataDir: _testDir,
            DownloadDirectory: null,
            WasAutoDetected: false
        );
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            5,
            5,
            "Paused ( 100% )",
            true
        );

        var exporter = new NdmExporter();
        var result = exporter.Export(
            task,
            location,
            _workingRoot,
            Path.Combine(_testDir, "out.mdma")
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void Export_Fails_Cleanly_When_MetadataDir_Missing()
    {
        var location = new TargetAppLocation(
            TargetApp.NDM,
            _testDir,
            MetadataDir: null,
            DownloadDirectory: null,
            WasAutoDetected: false
        );
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            5,
            5,
            "Paused ( 100% )",
            true
        );

        var exporter = new NdmExporter();
        var result = exporter.Export(
            task,
            location,
            _workingRoot,
            Path.Combine(_testDir, "out.mdma")
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void Export_Fails_Cleanly_When_SegmentsBin_Missing()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "temp", "1"));
        var location = new TargetAppLocation(
            TargetApp.NDM,
            Path.Combine(_testDir, "temp"),
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: false
        );
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            5,
            5,
            "Paused ( 100% )",
            true
        );

        var exporter = new NdmExporter();
        var result = exporter.Export(
            task,
            location,
            _workingRoot,
            Path.Combine(_testDir, "out.mdma")
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }

    [Test]
    public void Export_Fails_Cleanly_When_Segment_Data_File_Missing()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            5,
            (0, 4, 5)
        );
        fixture.Build();
        File.Delete(Path.Combine(fixture.TempDirectory, "1", "seg.x0")); // segments.bin still references it

        var location = new TargetAppLocation(
            TargetApp.NDM,
            fixture.TempDirectory,
            _testDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );
        var task = MakeSummary(1, "f.bin", "https://example.com/f.bin", 5, 5);

        var exporter = new NdmExporter();
        var result = exporter.Export(
            task,
            location,
            _workingRoot,
            Path.Combine(_testDir, "out.mdma")
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ExportFailed));
    }
}
