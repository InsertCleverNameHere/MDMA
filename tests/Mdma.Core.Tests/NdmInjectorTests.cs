using Mdma.Core.Tests.Fixtures;
using Microsoft.Data.Sqlite;

namespace Mdma.Core.Tests;

public class NdmInjectorTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-ndminjector-test-" + Guid.NewGuid());
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

    /// <summary>Builds a real, checksum-valid .mdma via the production writer,
    /// then loads it back via the production loader -- so tests exercise
    /// NdmInjector against a genuine MdmaPackage, not a hand-rolled fake.</summary>
    private MdmaPackage BuildLoadedPackage(string filename, string url, long totalBytes, string? mimeType, (long start, long end, byte[] bytes)[] chunks)
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
        var writer = new MdmaPackageWriter();
        writer.WritePackage(TargetApp.JD2, url, filename, totalBytes, mimeType,
            Array.Empty<KeyValuePair<string, string>>(), 1785268000000L, sources, mdmaPath);

        var loader = new MdmaLoader();
        return loader.Load(mdmaPath, _workingRoot).Value!;
    }

    private (NdmFixtureBuilder Fixture, TargetAppLocation Location) SetUpEmptyNdmEnvironment()
    {
        // Empty fixture: creates neatdb.db with the correct schema but zero tasks.
        var fixture = new NdmFixtureBuilder(_testDir);
        fixture.Build();
        var location = new TargetAppLocation(TargetApp.NDM, fixture.TempDirectory, _testDir, DownloadDirectory: fixture.DownloadDirectory, WasAutoDetected: true);
        return (fixture, location);
    }

    private static List<(long id, string url, string filename, long filesize, string status, string? folderpath, string? temppath, string? mimetype)> ReadAllDownloadRows(string dbPath)
    {
        var results = new List<(long, string, string, long, string, string?, string?, string?)>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, url, filename, filesize, status, folderpath, temppath, mimetype FROM downloads;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        SqliteConnection.ClearPool(conn);
        return results;
    }

    [Test]
    public void Inject_Writes_Segment_Files_With_Correct_Bytes()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        var chunk0Bytes = new byte[] { 1, 2, 3, 4, 5 };
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 5, null, new[] { (0L, 4L, chunk0Bytes) });

        var registry = new FakeRegistryAccessor();
        var injector = new NdmInjector(registry, new AtomicWriter());

        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.True);
        var newTaskDir = Path.Combine(fixture.TempDirectory, "1");
        Assert.That(File.ReadAllBytes(Path.Combine(newTaskDir, "seg.x0")), Is.EqualTo(chunk0Bytes));
    }

    [Test]
    public void Inject_Synthesizes_Correct_SegmentsBin()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 20, null,
            new[] { (0L, 9L, new byte[10]), (10L, 19L, new byte[10]) });

        var registry = new FakeRegistryAccessor();
        var injector = new NdmInjector(registry, new AtomicWriter());
        injector.Inject(package, location);

        var segmentsBinPath = Path.Combine(fixture.TempDirectory, "1", "segments.bin");
        var bytes = File.ReadAllBytes(segmentsBinPath);
        Assert.That(bytes, Has.Length.EqualTo(48)); // 2 records * 24 bytes

        var segment0Start = BitConverter.ToUInt64(bytes, 8);
        var segment0End = BitConverter.ToUInt64(bytes, 16);
        Assert.That(segment0Start, Is.EqualTo(0UL));
        Assert.That(segment0End, Is.EqualTo(9UL));
    }

    [Test]
    public void Inject_Inserts_Db_Row_With_Correct_Status_Percentage()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        // 10 total bytes, only 5 downloaded -> 50%
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 10, "application/octet-stream",
            new[] { (0L, 9L, new byte[5]) });

        var registry = new FakeRegistryAccessor();
        var injector = new NdmInjector(registry, new AtomicWriter());
        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.True);
        var rows = ReadAllDownloadRows(fixture.NeatDbPath);
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].status, Is.EqualTo("Paused ( 50% )"));
        Assert.That(rows[0].mimetype, Is.EqualTo("application/octet-stream"));
        Assert.That(rows[0].url, Is.EqualTo("https://example.com/f.bin"));
        Assert.That(rows[0].filename, Is.EqualTo("f.bin"));
    }

    [Test]
    public void Inject_Inserts_Headers_From_Manifest()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();

        // Build a package with headers directly (BuildLoadedPackage helper doesn't
        // pass headers through, so construct via writer directly here).
        var stagingDir = Path.Combine(_testDir, "staging2");
        Directory.CreateDirectory(stagingDir);
        var chunkPath = Path.Combine(stagingDir, "c0.bin");
        File.WriteAllBytes(chunkPath, new byte[] { 1, 2, 3 });
        var mdmaPath = Path.Combine(_testDir, "with-headers.mdma");
        new MdmaPackageWriter().WritePackage(
            TargetApp.NDM, "https://example.com/f.bin", "f.bin", 3, null,
            new[] { new KeyValuePair<string, string>("Referer", "https://example.com") },
            0, new[] { new MdmaChunkSource(0, 0, 2, chunkPath) }, mdmaPath);
        var package = new MdmaLoader().Load(mdmaPath, _workingRoot).Value!;

        var registry = new FakeRegistryAccessor();
        var injector = new NdmInjector(registry, new AtomicWriter());
        injector.Inject(package, location);

        using var conn = new SqliteConnection($"Data Source={fixture.NeatDbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT header FROM headers WHERE id = 1;";
        using var reader = cmd.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Referer: https://example.com"));
        SqliteConnection.ClearPool(conn);
    }

    [Test]
    public void Inject_Uses_LastDownloadId_Plus_One_As_New_Task_Id()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 3, null, new[] { (0L, 2L, new byte[3]) });

        var registry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "LastDownloadID", 520);
        var injector = new NdmInjector(registry, new AtomicWriter());

        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Directory.Exists(Path.Combine(fixture.TempDirectory, "521")), Is.True);
    }

    [Test]
    public void Inject_Defaults_To_Task_Id_1_When_Registry_Has_No_LastDownloadId()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 3, null, new[] { (0L, 2L, new byte[3]) });

        var registry = new FakeRegistryAccessor(); // nothing seeded
        var injector = new NdmInjector(registry, new AtomicWriter());
        injector.Inject(package, location);

        Assert.That(Directory.Exists(Path.Combine(fixture.TempDirectory, "1")), Is.True);
    }

    [Test]
    public void Inject_Updates_Registry_LastDownloadId_After_Success()
    {
        var (_, location) = SetUpEmptyNdmEnvironment();
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 3, null, new[] { (0L, 2L, new byte[3]) });

        var registry = new FakeRegistryAccessor();
        var injector = new NdmInjector(registry, new AtomicWriter());
        injector.Inject(package, location);

        Assert.That(registry.ReadDword(@"SOFTWARE\NeatDM", "LastDownloadID"), Is.EqualTo(1));
    }

    [Test]
    public void Inject_Fails_Cleanly_When_InstallOrConfigDir_Missing()
    {
        var location = new TargetAppLocation(TargetApp.NDM, InstallOrConfigDir: null, MetadataDir: _testDir, DownloadDirectory: null, WasAutoDetected: false);
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 3, null, new[] { (0L, 2L, new byte[3]) });

        var injector = new NdmInjector(new FakeRegistryAccessor(), new AtomicWriter());
        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }

    [Test]
    public void Inject_Fails_Cleanly_When_MetadataDir_Missing()
    {
        var location = new TargetAppLocation(TargetApp.NDM, _testDir, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: false);
        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 3, null, new[] { (0L, 2L, new byte[3]) });

        var injector = new NdmInjector(new FakeRegistryAccessor(), new AtomicWriter());
        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }

    [Test]
    public void Inject_Fails_When_Computed_Task_Directory_Already_Exists()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        Directory.CreateDirectory(Path.Combine(fixture.TempDirectory, "1")); // pre-existing conflict

        var package = BuildLoadedPackage("f.bin", "https://example.com/f.bin", 3, null, new[] { (0L, 2L, new byte[3]) });
        var injector = new NdmInjector(new FakeRegistryAccessor(), new AtomicWriter());
        var result = injector.Inject(package, location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InjectionFailed));
    }

    [Test]
    public void Inject_Two_Tasks_Sequentially_Gets_Distinct_Incrementing_Ids()
    {
        var (fixture, location) = SetUpEmptyNdmEnvironment();
        var registry = new FakeRegistryAccessor();
        var injector = new NdmInjector(registry, new AtomicWriter());

        var package1 = BuildLoadedPackage("a.bin", "https://example.com/a.bin", 3, null, new[] { (0L, 2L, new byte[3]) });
        injector.Inject(package1, location);

        var package2 = BuildLoadedPackage("b.bin", "https://example.com/b.bin", 3, null, new[] { (0L, 2L, new byte[3]) });
        injector.Inject(package2, location);

        var rows = ReadAllDownloadRows(fixture.NeatDbPath);
        Assert.That(rows.Select(r => r.id), Is.EquivalentTo(new long[] { 1, 2 }));
    }
}
