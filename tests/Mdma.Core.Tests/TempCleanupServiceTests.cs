namespace Mdma.Core.Tests;

public class TempCleanupServiceTests
{
    private string _testDir = null!;
    private WorkingRoot _workingRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-tempcleanup-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _workingRoot = new WorkingRoot(_testDir, true, false);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private string TmpDir => Path.Combine(_testDir, ".mdma-tmp");

    [Test]
    public void SweepOrphans_Returns_Empty_Report_When_MdmaTmp_Does_Not_Exist()
    {
        var service = new TempCleanupService();
        var result = service.SweepOrphans(_workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Removed, Is.Empty);
        Assert.That(result.Value.FailedToRemove, Is.Empty);
    }

    [Test]
    public void SweepOrphans_Returns_Empty_Report_When_MdmaTmp_Is_Empty()
    {
        Directory.CreateDirectory(TmpDir);
        var service = new TempCleanupService();

        var result = service.SweepOrphans(_workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Removed, Is.Empty);
    }

    [Test]
    public void SweepOrphans_Removes_Orphaned_Files()
    {
        Directory.CreateDirectory(TmpDir);
        var orphanFile = Path.Combine(TmpDir, "abc123.mdma");
        File.WriteAllText(orphanFile, "leftover");

        var service = new TempCleanupService();
        var result = service.SweepOrphans(_workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Removed, Has.Count.EqualTo(1));
        Assert.That(File.Exists(orphanFile), Is.False);
    }

    [Test]
    public void SweepOrphans_Removes_Orphaned_Folders_Recursively()
    {
        Directory.CreateDirectory(TmpDir);
        var orphanFolder = Path.Combine(TmpDir, "extracted-abc123");
        Directory.CreateDirectory(orphanFolder);
        File.WriteAllText(Path.Combine(orphanFolder, "chunk_0.bin"), "leftover chunk");

        var service = new TempCleanupService();
        var result = service.SweepOrphans(_workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Removed, Has.Count.EqualTo(1));
        Assert.That(Directory.Exists(orphanFolder), Is.False);
    }

    [Test]
    public void SweepOrphans_Removes_Multiple_Mixed_Entries()
    {
        Directory.CreateDirectory(TmpDir);
        File.WriteAllText(Path.Combine(TmpDir, "a.mdma"), "x");
        File.WriteAllText(Path.Combine(TmpDir, "b.mdma"), "x");
        Directory.CreateDirectory(Path.Combine(TmpDir, "jd2-export-xyz"));

        var service = new TempCleanupService();
        var result = service.SweepOrphans(_workingRoot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Removed, Has.Count.EqualTo(3));
        Assert.That(Directory.EnumerateFileSystemEntries(TmpDir), Is.Empty);
    }

    [Test]
    public void SweepOrphans_Reports_Removed_Paths_Correctly()
    {
        Directory.CreateDirectory(TmpDir);
        var orphanFile = Path.Combine(TmpDir, "leftover.mdma");
        File.WriteAllText(orphanFile, "x");

        var service = new TempCleanupService();
        var result = service.SweepOrphans(_workingRoot);

        Assert.That(result.Value!.Removed.Single(), Does.Contain("leftover.mdma"));
    }

    [Test]
    public void SweepOrphans_Does_Not_Touch_Files_Outside_MdmaTmp()
    {
        Directory.CreateDirectory(TmpDir);
        var outsideFile = Path.Combine(_testDir, "should-survive.txt");
        File.WriteAllText(outsideFile, "important");
        File.WriteAllText(Path.Combine(TmpDir, "orphan.mdma"), "x");

        var service = new TempCleanupService();
        service.SweepOrphans(_workingRoot);

        Assert.That(File.Exists(outsideFile), Is.True);
    }
}
