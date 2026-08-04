using System.IO.Compression;
using System.Text.Json;
using Mdma.Core.Tests.Fixtures;
using Microsoft.Data.Sqlite;

namespace Mdma.Core.Tests;

/// <summary>
/// True end-to-end round-trip tests, per mdma-core-plan.md §5.6 (the coverage
/// gap flagged at the end of Phase 4). Uses real exporters, real injectors,
/// real MdmaPackageWriter/MdmaLoader, real BackupManager, all driven through
/// ConversionService.ConvertSameMachine -- not fakes/spies. Source and
/// destination are separate fixture roots, standing in for "two machines"
/// conceptually even though the conversion is mechanically same-machine.
///
/// These tests rely on the fresh-install fix to BackupManager/NdmInjector
/// (missing neatdb.db / downloadList*.zip is a no-op backup, and NdmInjector
/// creates a fresh db if none exists) -- destinations below are genuinely
/// empty environments, not pre-seeded with an unrelated task.
/// </summary>
public class RoundTripTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-roundtrip-test-" + Guid.NewGuid());
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

    private ConversionService CreateRealService() =>
        new(
            _workingRoot,
            new ProcessGuard(new FakeProcessLister()), // no processes "running" -- never blocks
            new SpaceChecker(new FakeDiskSpaceSource { FreeBytes = long.MaxValue }),
            new BackupManager(new FakeClock()),
            exporters: new Dictionary<TargetApp, IMdmaExporter>
            {
                [TargetApp.NDM] = new NdmExporter(),
                [TargetApp.JD2] = new Jd2Exporter(),
            },
            injectors: new Dictionary<TargetApp, IDownloadListInjector>
            {
                [TargetApp.NDM] = new NdmInjector(new FakeRegistryAccessor(), new AtomicWriter()),
                [TargetApp.JD2] = new Jd2Injector(new AtomicWriter()),
            },
            mdmaLoader: new MdmaLoader()
        );

    // ---------- NDM -> JD2 ----------

    [Test]
    public void RoundTrip_Ndm_To_Jd2_Fully_Downloaded_Task()
    {
        var sourceRoot = Path.Combine(_testDir, "ndm-source");
        var chunk0 = new byte[10];
        var chunk1 = new byte[10];
        new Random(1).NextBytes(chunk0);
        new Random(2).NextBytes(chunk1);
        var ndmFixture = new NdmFixtureBuilder(sourceRoot).WithTask(
            521,
            "poc.bin",
            "https://example.com/poc.bin",
            20,
            (0, 9, 10),
            (10, 19, 10)
        );
        ndmFixture.Build();
        // Overwrite with known bytes so we can assert exact content later.
        File.WriteAllBytes(Path.Combine(ndmFixture.TempDirectory, "521", "seg.x0"), chunk0);
        File.WriteAllBytes(Path.Combine(ndmFixture.TempDirectory, "521", "seg.x1"), chunk1);

        var sourceLocation = new TargetAppLocation(
            TargetApp.NDM,
            ndmFixture.TempDirectory,
            sourceRoot,
            DownloadDirectory: ndmFixture.DownloadDirectory,
            WasAutoDetected: true
        );
        var task = new DownloadTaskSummary(
            "521",
            TargetApp.NDM,
            "poc.bin",
            "https://example.com/poc.bin",
            20,
            20,
            "Paused ( 100% )",
            true
        );

        var destCfgDir = Path.Combine(_testDir, "jd2-dest", "cfg");
        var destDownloadFolder = Path.Combine(_testDir, "jd2-dest", "downloads");
        Directory.CreateDirectory(destCfgDir);
        var destLocation = new TargetAppLocation(
            TargetApp.JD2,
            destCfgDir,
            MetadataDir: null,
            DownloadDirectory: destDownloadFolder,
            WasAutoDetected: true
        );

        var service = CreateRealService();
        var result = service.ConvertSameMachine(task, sourceLocation, destLocation);

        Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());

        var newZipPath = Path.Combine(destCfgDir, "downloadList1.zip");
        Assert.That(File.Exists(newZipPath), Is.True);

        using (var zip = ZipFile.OpenRead(newZipPath))
        {
            var linkEntry = zip.Entries.Single(e => e.Name.Contains('_'));
            using var stream = linkEntry.Open();
            using var doc = JsonDocument.Parse(stream);
            Assert.That(doc.RootElement.GetProperty("size").GetInt64(), Is.EqualTo(20));
            Assert.That(doc.RootElement.GetProperty("current").GetInt64(), Is.EqualTo(20));
        }

        var partBytes = File.ReadAllBytes(Path.Combine(destDownloadFolder, "poc.bin.part"));
        Assert.That(partBytes[0..10], Is.EqualTo(chunk0));
        Assert.That(partBytes[10..20], Is.EqualTo(chunk1));
    }

    [Test]
    public void RoundTrip_Ndm_To_Jd2_Partial_Download_Task()
    {
        var sourceRoot = Path.Combine(_testDir, "ndm-source");
        // 20-byte file, only the first chunk (10 bytes) fully downloaded, second chunk not started.
        var chunk0 = new byte[10];
        new Random(3).NextBytes(chunk0);
        var ndmFixture = new NdmFixtureBuilder(sourceRoot).WithTask(
            1,
            "partial.bin",
            "https://example.com/partial.bin",
            20,
            (0, 9, 10),
            (10, 19, 0)
        );
        ndmFixture.Build();
        File.WriteAllBytes(Path.Combine(ndmFixture.TempDirectory, "1", "seg.x0"), chunk0);
        // seg.x1 stays empty (0 bytes), matching the 0-downloaded chunk.

        var sourceLocation = new TargetAppLocation(
            TargetApp.NDM,
            ndmFixture.TempDirectory,
            sourceRoot,
            DownloadDirectory: ndmFixture.DownloadDirectory,
            WasAutoDetected: true
        );
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "partial.bin",
            "https://example.com/partial.bin",
            20,
            10,
            "Paused ( 50% )",
            true
        );

        var destCfgDir = Path.Combine(_testDir, "jd2-dest", "cfg");
        var destDownloadFolder = Path.Combine(_testDir, "jd2-dest", "downloads");
        Directory.CreateDirectory(destCfgDir);
        var destLocation = new TargetAppLocation(
            TargetApp.JD2,
            destCfgDir,
            MetadataDir: null,
            DownloadDirectory: destDownloadFolder,
            WasAutoDetected: true
        );

        var service = CreateRealService();
        var result = service.ConvertSameMachine(task, sourceLocation, destLocation);

        Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());

        using var zip = ZipFile.OpenRead(Path.Combine(destCfgDir, "downloadList1.zip"));
        var linkEntry = zip.Entries.Single(e => e.Name.Contains('_'));
        using var stream = linkEntry.Open();
        using var doc = JsonDocument.Parse(stream);
        Assert.That(
            doc.RootElement.GetProperty("current").GetInt64(),
            Is.EqualTo(10),
            "only 10 of 20 bytes were downloaded"
        );

        var partBytes = File.ReadAllBytes(Path.Combine(destDownloadFolder, "partial.bin.part"));
        Assert.That(partBytes[0..10], Is.EqualTo(chunk0));
    }

    // ---------- JD2 -> NDM ----------

    [Test]
    public void RoundTrip_Jd2_To_Ndm_Fully_Downloaded_Task()
    {
        var sourceDownloadFolder = Path.Combine(_testDir, "jd2-source", "downloads");
        Directory.CreateDirectory(sourceDownloadFolder);
        var fileBytes = new byte[20];
        new Random(4).NextBytes(fileBytes);
        File.WriteAllBytes(Path.Combine(sourceDownloadFolder, "complete.bin.part"), fileBytes);

        var jd2Fixture = new Jd2FixtureBuilder(Path.Combine(_testDir, "jd2-source"))
            .WithPackageDownloadFolder("99", sourceDownloadFolder)
            .WithLink("99", "00", "complete.bin", "https://example.com/complete.bin", 20, 20, 20);
        jd2Fixture.Build();

        var sourceLocation = new TargetAppLocation(
            TargetApp.JD2,
            jd2Fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: sourceDownloadFolder,
            WasAutoDetected: true
        );
        var task = new DownloadTaskSummary(
            "99_00",
            TargetApp.JD2,
            "complete.bin",
            "https://example.com/complete.bin",
            20,
            20,
            "Paused ( 100% )",
            true
        );

        // Genuinely fresh NDM destination -- no neatdb.db exists yet, exercising the fresh-install fix.
        var destTempDir = Path.Combine(_testDir, "ndm-dest", "temp");
        var destMetaDir = Path.Combine(_testDir, "ndm-dest", "meta");
        Directory.CreateDirectory(destTempDir);
        Directory.CreateDirectory(destMetaDir);
        var destLocation = new TargetAppLocation(
            TargetApp.NDM,
            destTempDir,
            destMetaDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var service = CreateRealService();
        var result = service.ConvertSameMachine(task, sourceLocation, destLocation);

        Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());

        var dbPath = Path.Combine(destMetaDir, "neatdb.db");
        Assert.That(File.Exists(dbPath), Is.True);

        using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT filename, filesize, status FROM downloads WHERE id = 1;";
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("complete.bin"));
            Assert.That(reader.GetInt64(1), Is.EqualTo(20));
            Assert.That(reader.GetString(2), Is.EqualTo("Paused ( 100% )"));
            SqliteConnection.ClearPool(conn);
        }

        var segBytes = File.ReadAllBytes(Path.Combine(destTempDir, "1", "seg.x0"));
        Assert.That(segBytes, Is.EqualTo(fileBytes));
    }

    [Test]
    public void RoundTrip_Jd2_To_Ndm_Partial_Download_Task()
    {
        var sourceDownloadFolder = Path.Combine(_testDir, "jd2-source", "downloads");
        Directory.CreateDirectory(sourceDownloadFolder);
        // 20-byte file, only 8 bytes actually written so far.
        var partialBytes = new byte[8];
        new Random(5).NextBytes(partialBytes);
        File.WriteAllBytes(Path.Combine(sourceDownloadFolder, "partial.bin.part"), partialBytes);

        var jd2Fixture = new Jd2FixtureBuilder(Path.Combine(_testDir, "jd2-source"))
            .WithPackageDownloadFolder("50", sourceDownloadFolder)
            .WithLink("50", "00", "partial.bin", "https://example.com/partial.bin", 20, 8, 8);
        jd2Fixture.Build();

        var sourceLocation = new TargetAppLocation(
            TargetApp.JD2,
            jd2Fixture.CfgDirectory,
            MetadataDir: null,
            DownloadDirectory: sourceDownloadFolder,
            WasAutoDetected: true
        );
        var task = new DownloadTaskSummary(
            "50_00",
            TargetApp.JD2,
            "partial.bin",
            "https://example.com/partial.bin",
            20,
            8,
            "Paused ( 40% )",
            true
        );

        var destTempDir = Path.Combine(_testDir, "ndm-dest", "temp");
        var destMetaDir = Path.Combine(_testDir, "ndm-dest", "meta");
        Directory.CreateDirectory(destTempDir);
        Directory.CreateDirectory(destMetaDir);
        var destLocation = new TargetAppLocation(
            TargetApp.NDM,
            destTempDir,
            destMetaDir,
            DownloadDirectory: null,
            WasAutoDetected: true
        );

        var service = CreateRealService();
        var result = service.ConvertSameMachine(task, sourceLocation, destLocation);

        Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());

        var segBytes = File.ReadAllBytes(Path.Combine(destTempDir, "1", "seg.x0"));
        Assert.That(segBytes, Is.EqualTo(partialBytes));
        Assert.That(
            segBytes,
            Has.Length.EqualTo(8),
            "only the 8 actually-downloaded bytes should have been transferred"
        );
    }

    [Test]
    public void RoundTrip_Ndm_To_Jd2_Preserves_Url()
    {
        var sourceRoot = Path.Combine(_testDir, "ndm-source");
        var ndmFixture = new NdmFixtureBuilder(sourceRoot).WithTask(
            1,
            "f.bin",
            "https://distinctive-test-url.example.com/f.bin",
            5,
            (0, 4, 5)
        );
        ndmFixture.Build();

        var sourceLocation = new TargetAppLocation(
            TargetApp.NDM,
            ndmFixture.TempDirectory,
            sourceRoot,
            DownloadDirectory: ndmFixture.DownloadDirectory,
            WasAutoDetected: true
        );
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://distinctive-test-url.example.com/f.bin",
            5,
            5,
            "Paused ( 100% )",
            true
        );

        var destCfgDir = Path.Combine(_testDir, "jd2-dest", "cfg");
        Directory.CreateDirectory(destCfgDir);
        var destLocation = new TargetAppLocation(
            TargetApp.JD2,
            destCfgDir,
            MetadataDir: null,
            DownloadDirectory: Path.Combine(_testDir, "jd2-dest", "downloads"),
            WasAutoDetected: true
        );

        var service = CreateRealService();
        service.ConvertSameMachine(task, sourceLocation, destLocation);

        using var zip = ZipFile.OpenRead(Path.Combine(destCfgDir, "downloadList1.zip"));
        var linkEntry = zip.Entries.Single(e => e.Name.Contains('_'));
        using var stream = linkEntry.Open();
        using var doc = JsonDocument.Parse(stream);
        Assert.That(
            doc.RootElement.GetProperty("url").GetString(),
            Is.EqualTo("https://distinctive-test-url.example.com/f.bin")
        );
    }

    [Test]
    public void RoundTrip_With_FileLogger_Writes_Structured_Log_Entries_To_Disk()
    {
        var fileLogger = new FileLogger(_workingRoot);
        var service = new ConversionService(
            _workingRoot,
            new ProcessGuard(new FakeProcessLister()),
            new SpaceChecker(new FakeDiskSpaceSource { FreeBytes = long.MaxValue }),
            new BackupManager(new FakeClock(), fileLogger),
            exporters: new Dictionary<TargetApp, IMdmaExporter>
            {
                [TargetApp.NDM] = new NdmExporter(),
                [TargetApp.JD2] = new Jd2Exporter(),
            },
            injectors: new Dictionary<TargetApp, IDownloadListInjector>
            {
                [TargetApp.NDM] = new NdmInjector(new FakeRegistryAccessor(), new AtomicWriter()),
                [TargetApp.JD2] = new Jd2Injector(new AtomicWriter()),
            },
            mdmaLoader: new MdmaLoader(),
            logger: fileLogger
        );

        var sourceRoot = Path.Combine(_testDir, "ndm-source");
        var ndmFixture = new NdmFixtureBuilder(sourceRoot).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            10,
            (0, 9, 10)
        );
        ndmFixture.Build();
        var sourceLocation = new TargetAppLocation(
            TargetApp.NDM,
            ndmFixture.TempDirectory,
            sourceRoot,
            ndmFixture.DownloadDirectory,
            true
        );
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            10,
            10,
            "Paused ( 100% )",
            true
        );

        var destCfgDir = Path.Combine(_testDir, "jd2-dest", "cfg");
        Directory.CreateDirectory(destCfgDir);
        var destLocation = new TargetAppLocation(
            TargetApp.JD2,
            destCfgDir,
            null,
            Path.Combine(_testDir, "jd2-dest", "downloads"),
            true
        );

        var result = service.ConvertSameMachine(task, sourceLocation, destLocation);

        Assert.That(result.IsSuccess, Is.True);

        var logsDir = Path.Combine(_workingRoot.Path, "logs");
        Assert.That(Directory.Exists(logsDir), Is.True);
        var logFiles = Directory.GetFiles(logsDir, "*.log");
        Assert.That(logFiles, Has.Length.EqualTo(1));

        var lines = File.ReadAllLines(logFiles[0]);
        Assert.That(lines, Has.Length.AtLeast(2));
        Assert.That(
            lines.Any(l =>
                l.Contains("ConversionService") && l.Contains("Starting same-machine conversion")
            ),
            Is.True
        );
    }
}
