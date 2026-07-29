using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class NdmListReaderTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-ndmlistreader-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void ScanTasks_Returns_Correct_Summary_For_Single_Task()
    {
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(521, "poc_ndm_perfect.bin", "https://example.com/f.bin", 10_485_760,
                (0, 10_485_759, 2_097_152));
        fixture.Build();

        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, fixture.TempDirectory, _testDir, DownloadDirectory: null, WasAutoDetected: true);

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        var task = result.Value![0];
        Assert.That(task.NativeId, Is.EqualTo("521"));
        Assert.That(task.Filename, Is.EqualTo("poc_ndm_perfect.bin"));
        Assert.That(task.TotalBytes, Is.EqualTo(10_485_760));
        Assert.That(task.Resumable, Is.True);
    }

    [Test]
    public void ScanTasks_Computes_DownloadedBytes_From_Real_SegmentFile_Sizes()
    {
        // 2 chunks, downloaded 100 and 250 bytes respectively -> total 350,
        // must come from actual seg.x0/seg.x1 file sizes, not from the DB.
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(1, "f.bin", "https://example.com/f.bin", 10_000,
                (0, 4999, 100), (5000, 9999, 250));
        fixture.Build();

        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, fixture.TempDirectory, _testDir, DownloadDirectory: null, WasAutoDetected: true);

        var result = reader.ScanTasks(location);

        Assert.That(result.Value![0].DownloadedBytes, Is.EqualTo(350));
    }

    [Test]
    public void ScanTasks_Falls_Back_To_Status_Percentage_When_TempDir_Unavailable()
    {
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(1, "f.bin", "https://example.com/f.bin", 10_000,
                (0, 9999, 2_000)); // real files say 20%, but we'll hide the temp dir
        fixture.Build();

        var reader = new NdmListReader();
        // InstallOrConfigDir is null, simulating manual-path-validation location
        // (no known temp dir) -- must fall back to parsing "Paused ( 20% )".
        var location = new TargetAppLocation(TargetApp.NDM, InstallOrConfigDir: null, MetadataDir: _testDir, DownloadDirectory: null, WasAutoDetected: false);

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.True);
        // status was computed by the fixture as floor(2000/10000*100) = 20%
        // -> estimate = 10000 * 20 / 100 = 2000
        Assert.That(result.Value![0].DownloadedBytes, Is.EqualTo(2_000));
    }

    [Test]
    public void ScanTasks_Returns_Zero_When_TempDir_Available_But_Task_Directory_Missing()
    {
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(1, "f.bin", "https://example.com/f.bin", 10_000, (0, 9999, 2_000));
        fixture.Build();
        // Delete the task's directory after building, to simulate a DB row
        // whose on-disk segment files are gone.
        Directory.Delete(Path.Combine(fixture.TempDirectory, "1"), recursive: true);

        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, fixture.TempDirectory, _testDir, DownloadDirectory: null, WasAutoDetected: true);

        var result = reader.ScanTasks(location);

        Assert.That(result.Value![0].DownloadedBytes, Is.EqualTo(0));
    }

    [Test]
    public void ScanTasks_Handles_Multiple_Tasks()
    {
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(1, "a.bin", "https://example.com/a.bin", 1000, (0, 999, 500))
            .WithTask(2, "b.bin", "https://example.com/b.bin", 2000, (0, 1999, 2000));
        fixture.Build();

        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, fixture.TempDirectory, _testDir, DownloadDirectory: null, WasAutoDetected: true);

        var result = reader.ScanTasks(location);

        Assert.That(result.Value, Has.Count.EqualTo(2));
        Assert.That(result.Value!.Select(t => t.NativeId), Is.EquivalentTo(new[] { "1", "2" }));
    }

    [Test]
    public void ScanTasks_Fails_Cleanly_When_MetadataDir_Missing_On_Location()
    {
        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, _testDir, MetadataDir: null, DownloadDirectory: null, WasAutoDetected: true);

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ScanFailed));
    }

    [Test]
    public void ScanTasks_Fails_Cleanly_When_NeatDbFile_Missing()
    {
        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, _testDir, _testDir, DownloadDirectory: null, WasAutoDetected: true);

        var result = reader.ScanTasks(location);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ScanFailed));
    }

    [Test]
    public void ScanTasks_Does_Not_Lock_The_Db_File_Afterward()
    {
        var fixture = new NdmFixtureBuilder(_testDir)
            .WithTask(1, "f.bin", "https://example.com/f.bin", 1000, (0, 999, 500));
        fixture.Build();

        var reader = new NdmListReader();
        var location = new TargetAppLocation(TargetApp.NDM, fixture.TempDirectory, _testDir, DownloadDirectory: null, WasAutoDetected: true);
        reader.ScanTasks(location);

        Assert.DoesNotThrow(() => Directory.Delete(_testDir, recursive: true));
        Directory.CreateDirectory(_testDir);
    }
}
