namespace Mdma.Core.Tests;

public class AtomicWriterTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-atomicwriter-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void Successful_Write_Replaces_Nonexistent_Destination()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");

        var result = writer.WriteAtomic(destination, path => File.WriteAllText(path, "hello"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(destination), Is.True);
        Assert.That(File.ReadAllText(destination), Is.EqualTo("hello"));
    }

    [Test]
    public void Successful_Write_Replaces_Existing_Destination()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");
        File.WriteAllText(destination, "old content");

        var result = writer.WriteAtomic(destination, path => File.WriteAllText(path, "new content"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.ReadAllText(destination), Is.EqualTo("new content"));
    }

    [Test]
    public void Exception_Mid_Write_Leaves_Original_Destination_Unchanged()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");
        File.WriteAllText(destination, "original, must survive");

        var result = writer.WriteAtomic(destination, path =>
        {
            File.WriteAllText(path, "partial garbage");
            throw new InvalidOperationException("simulated failure mid-write");
        });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.AtomicWriteFailed));
        Assert.That(File.ReadAllText(destination), Is.EqualTo("original, must survive"));
    }

    [Test]
    public void Exception_Mid_Write_Leaves_No_Orphaned_Temp_File()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");

        writer.WriteAtomic(destination, path =>
        {
            File.WriteAllText(path, "partial garbage");
            throw new InvalidOperationException("simulated failure mid-write");
        });

        var leftoverFiles = Directory.GetFiles(_testDir);
        Assert.That(leftoverFiles, Is.Empty, "no temp file should remain after a failed write");
    }

    [Test]
    public void WriteAction_Producing_No_File_Returns_Typed_Error()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");

        // writeAction does nothing at all — never creates the temp file.
        var result = writer.WriteAtomic(destination, _ => { });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.AtomicWriteFailed));
        Assert.That(File.Exists(destination), Is.False);
    }

    [Test]
    public void Creates_Destination_Directory_If_Missing()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "nested", "sub", "output.txt");

        var result = writer.WriteAtomic(destination, path => File.WriteAllText(path, "content"));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(File.Exists(destination), Is.True);
    }

    [Test]
    public void Temp_File_Is_Created_In_Same_Directory_As_Destination()
    {
        // Documents the atomicity-critical design choice: temp file must share
        // the destination's directory so the finalizing rename is same-volume
        // and therefore atomic, not a cross-volume copy+delete masquerading as one.
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");
        string? observedTempDir = null;

        writer.WriteAtomic(destination, path =>
        {
            observedTempDir = Path.GetDirectoryName(path);
            File.WriteAllText(path, "content");
        });

        Assert.That(observedTempDir, Is.EqualTo(_testDir));
    }

    // Concurrency is explicitly out of scope for v1 (single-operation-at-a-time
    // per architecture.md and the Core plan). This is a documentation test, not
    // a guarantee: it asserts the class makes no attempt at locking, so nobody
    // mistakes its absence for an oversight later.
    [Test]
    public void Concurrent_Writes_To_Same_Destination_Are_Not_Guarded_By_This_Class()
    {
        var writer = new AtomicWriter();
        var destination = Path.Combine(_testDir, "output.txt");

        // Two sequential (not truly concurrent, but back-to-back) writes both
        // succeed with no locking/coordination between them — last write wins.
        // This is expected v1 behavior: callers (ConversionService) are
        // responsible for ensuring only one operation runs at a time.
        var first = writer.WriteAtomic(destination, path => File.WriteAllText(path, "first"));
        var second = writer.WriteAtomic(destination, path => File.WriteAllText(path, "second"));

        Assert.That(first.IsSuccess, Is.True);
        Assert.That(second.IsSuccess, Is.True);
        Assert.That(File.ReadAllText(destination), Is.EqualTo("second"));
    }
}
