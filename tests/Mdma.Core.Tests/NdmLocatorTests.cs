using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class NdmLocatorTests
{
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-ndmlocator-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Test]
    public void AutoDetect_Succeeds_When_Registry_Values_Present_And_Dir_Exists()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "file.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 200)
        );
        fixture.Build();

        var registry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", fixture.TempDirectory)
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", fixture.DownloadDirectory);

        var locator = new NdmLocator(registry);
        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.InstallOrConfigDir, Is.EqualTo(fixture.TempDirectory));
        Assert.That(result.Value.DownloadDirectory, Is.EqualTo(fixture.DownloadDirectory));
        Assert.That(result.Value.MetadataDir, Is.Not.Null.And.Contains("NeatDM"));
        Assert.That(result.Value.WasAutoDetected, Is.True);
    }

    [Test]
    public void AutoDetect_Fails_Cleanly_When_Registry_Values_Missing()
    {
        var registry = new FakeRegistryAccessor(); // nothing seeded
        var locator = new NdmLocator(registry);

        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppNotFound));
    }

    [Test]
    public void AutoDetect_Fails_Cleanly_When_Registry_Points_At_Missing_Directory()
    {
        var registry = new FakeRegistryAccessor()
            .Seed(@"SOFTWARE\NeatDM", "TempDirectory", Path.Combine(_testDir, "does-not-exist"))
            .Seed(@"SOFTWARE\NeatDM", "DownloadDirectory", _testDir);

        var locator = new NdmLocator(registry);
        var result = locator.TryAutoDetect();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.TargetAppNotFound));
    }

    [Test]
    public void ValidateManualPath_Succeeds_Against_Valid_Fixture()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "file.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 200)
        );
        fixture.Build();

        var locator = new NdmLocator(new FakeRegistryAccessor());
        var result = locator.ValidateManualPath(_testDir);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void ValidateManualPath_Fails_When_Directory_Does_Not_Exist()
    {
        var locator = new NdmLocator(new FakeRegistryAccessor());
        var result = locator.ValidateManualPath(Path.Combine(_testDir, "nope"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Fails_When_NeatDbFile_Missing()
    {
        // real directory, but no neatdb.db in it
        var locator = new NdmLocator(new FakeRegistryAccessor());
        var result = locator.ValidateManualPath(_testDir);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Fails_When_File_Is_Not_A_Valid_Sqlite_Db()
    {
        File.WriteAllText(Path.Combine(_testDir, "neatdb.db"), "this is not a database");

        var locator = new NdmLocator(new FakeRegistryAccessor());
        var result = locator.ValidateManualPath(_testDir);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Fails_When_Downloads_Table_Missing()
    {
        // Build a real sqlite db but without the expected schema.
        var dbPath = Path.Combine(_testDir, "neatdb.db");
        using (
            var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={dbPath};Pooling=False"
            )
        )
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE unrelated_table (id INTEGER);";
            cmd.ExecuteNonQuery();
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(conn);
        }

        var locator = new NdmLocator(new FakeRegistryAccessor());
        var result = locator.ValidateManualPath(_testDir);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.ManualPathInvalid));
    }

    [Test]
    public void ValidateManualPath_Does_Not_Lock_The_Db_File_Afterward()
    {
        // Regression guard for the same pooling issue we hit with NdmFixtureBuilder:
        // validation must not leave a handle open that blocks cleanup.
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "file.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 200)
        );
        fixture.Build();

        var locator = new NdmLocator(new FakeRegistryAccessor());
        locator.ValidateManualPath(_testDir);

        Assert.DoesNotThrow(() => Directory.Delete(_testDir, recursive: true));
        Directory.CreateDirectory(_testDir); // recreate so TearDown doesn't fail
    }

    [Test]
    public void ValidateManualPath_Leaves_InstallOrConfigDir_Null_But_Sets_MetadataDir()
    {
        var fixture = new NdmFixtureBuilder(_testDir).WithTask(
            521,
            "file.bin",
            "https://example.com/f.bin",
            1000,
            (0, 999, 200)
        );
        fixture.Build();

        var locator = new NdmLocator(new FakeRegistryAccessor());
        var result = locator.ValidateManualPath(_testDir);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.InstallOrConfigDir, Is.Null);
        Assert.That(result.Value.MetadataDir, Is.EqualTo(_testDir));
    }
}
