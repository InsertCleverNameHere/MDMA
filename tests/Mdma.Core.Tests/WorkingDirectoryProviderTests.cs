namespace Mdma.Core.Tests;

public class WorkingDirectoryProviderTests
{
    private string _testRoot = null!;
    private string _fakeBaseDir = null!;
    private string _fakeAppDataDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "mdma-wdp-test-" + Guid.NewGuid());
        _fakeBaseDir = Path.Combine(_testRoot, "exe-dir");
        _fakeAppDataDir = Path.Combine(_testRoot, "appdata");
        Directory.CreateDirectory(_fakeBaseDir);
        Directory.CreateDirectory(_fakeAppDataDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Test]
    public void ExplicitOverride_Honored_When_Writable()
    {
        var overridePath = Path.Combine(_testRoot, "custom-workdir");
        var provider = new WorkingDirectoryProvider(_fakeBaseDir, _fakeAppDataDir);

        var result = provider.Resolve(overridePath);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Path, Is.EqualTo(overridePath));
        Assert.That(result.Value.IsPortableDefault, Is.False);
        Assert.That(result.Value.IsFallback, Is.False);
        Assert.That(Directory.Exists(overridePath), Is.True);
    }

    [Test]
    public void FallsThrough_To_PortableDefault_When_No_Override_Given()
    {
        var provider = new WorkingDirectoryProvider(_fakeBaseDir, _fakeAppDataDir);

        var result = provider.Resolve(null);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Path, Is.EqualTo(Path.Combine(_fakeBaseDir, "MDMA_Work")));
        Assert.That(result.Value.IsPortableDefault, Is.True);
        Assert.That(result.Value.IsFallback, Is.False);
    }

    [Test]
    public void FallsThrough_To_AppDataFallback_When_PortableDefault_Unwritable()
    {
        // Simulate an unwritable exe directory by pointing baseDirectory at a
        // path that can never be created as a directory (a file sitting where
        // the portable folder would need to go).
        var blockedBaseDir = Path.Combine(_testRoot, "blocked-exe-dir");
        Directory.CreateDirectory(blockedBaseDir);
        var blockingFile = Path.Combine(blockedBaseDir, "MDMA_Work");
        File.WriteAllText(blockingFile, "blocking file, not a directory");

        var provider = new WorkingDirectoryProvider(blockedBaseDir, _fakeAppDataDir);

        var result = provider.Resolve(null);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Path, Is.EqualTo(Path.Combine(_fakeAppDataDir, "MDMA", "work")));
        Assert.That(result.Value.IsPortableDefault, Is.False);
        Assert.That(result.Value.IsFallback, Is.True);
    }

    [Test]
    public void ExplicitOverride_Rejected_When_Path_Cannot_Be_Created()
    {
        // Point the override at a path nested under a file (not a directory),
        // which Directory.CreateDirectory cannot resolve.
        var blockingFile = Path.Combine(_testRoot, "not-a-directory");
        File.WriteAllText(blockingFile, "blocking file");
        var badOverride = Path.Combine(blockingFile, "workdir");

        var provider = new WorkingDirectoryProvider(_fakeBaseDir, _fakeAppDataDir);

        var result = provider.Resolve(badOverride);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.WorkingDirectoryUnwritable));
    }

    [Test]
    public void Resolve_Is_Idempotent_For_Same_Override()
    {
        var overridePath = Path.Combine(_testRoot, "custom-workdir");
        var provider = new WorkingDirectoryProvider(_fakeBaseDir, _fakeAppDataDir);

        var first = provider.Resolve(overridePath);
        var second = provider.Resolve(overridePath);

        Assert.That(first.Value!.Path, Is.EqualTo(second.Value!.Path));
        Assert.That(first.IsSuccess, Is.EqualTo(second.IsSuccess));
    }
}
