using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class LocationResolverTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-locresolver-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void ResolveLocation_AutoDetect_Succeeds_And_Applies_Overrides()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var fakeRegistry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var resolver = new LocationResolver(fakeRegistry, appDataDirectory: _testDir);

        var result = resolver.ResolveLocation(
            TargetApp.NDM,
            manualPathOverride: null,
            metadataDirOverride: _testDir,
            tempDirOverride: @"D:\OverrideTemp",
            downloadDirOverride: @"D:\OverrideDownload"
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.MetadataDir, Is.EqualTo(_testDir));
        Assert.That(result.Value.InstallOrConfigDir, Is.EqualTo(@"D:\OverrideTemp"));
        Assert.That(result.Value.DownloadDirectory, Is.EqualTo(@"D:\OverrideDownload"));
    }

    [Test]
    public void ResolveLocation_ManualPath_Succeeds_And_Applies_Overrides()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            1,
            "f.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 500)
        );
        fixture.Build();

        var resolver = new LocationResolver(new FakeRegistryAccessor());

        var result = resolver.ResolveLocation(
            TargetApp.NDM,
            manualPathOverride: _testDir,
            tempDirOverride: fixture.TempDirectory
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.MetadataDir, Is.EqualTo(_testDir));
        Assert.That(result.Value.InstallOrConfigDir, Is.EqualTo(fixture.TempDirectory));
    }

    [Test]
    public void ResolveLocation_Fails_When_AutoDetect_Fails()
    {
        var resolver = new LocationResolver(new FakeRegistryAccessor(), appDataDirectory: _testDir);

        var result = resolver.ResolveLocation(TargetApp.NDM);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppNotFound));
    }
}
