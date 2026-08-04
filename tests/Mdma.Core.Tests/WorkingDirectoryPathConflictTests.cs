namespace Mdma.Core.Tests;

public class WorkingDirectoryPathConflictTests
{
    private string _testDir = null!;
    private WorkingDirectoryProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "mdma-pathconflict-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _provider = new WorkingDirectoryProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private static TargetAppLocation MakeLocation(TargetApp app, string? installDir, string? metadataDir) =>
        new(app, installDir, metadataDir, DownloadDirectory: null, WasAutoDetected: true);

    [Test]
    public void Rejects_When_WorkingRoot_Is_Nested_Inside_InstallOrConfigDir()
    {
        var appDir = Path.Combine(_testDir, "app");
        var workingRootPath = Path.Combine(appDir, "MDMA_Work");
        Directory.CreateDirectory(workingRootPath);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);
        var location = MakeLocation(TargetApp.NDM, appDir, null);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.WorkingDirectoryPathConflict));
    }

    [Test]
    public void Rejects_When_WorkingRoot_Is_Nested_Inside_MetadataDir()
    {
        var appDir = Path.Combine(_testDir, "app");
        var workingRootPath = Path.Combine(appDir, "nested", "MDMA_Work");
        Directory.CreateDirectory(workingRootPath);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);
        var location = MakeLocation(TargetApp.NDM, null, appDir);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.WorkingDirectoryPathConflict));
    }

    [Test]
    public void Rejects_When_AppDirectory_Is_Nested_Inside_WorkingRoot()
    {
        var workingRootPath = Path.Combine(_testDir, "MDMA_Work");
        var appDir = Path.Combine(workingRootPath, "some", "app", "dir");
        Directory.CreateDirectory(appDir);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);
        var location = MakeLocation(TargetApp.JD2, appDir, null);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.WorkingDirectoryPathConflict));
    }

    [Test]
    public void Rejects_When_WorkingRoot_Exactly_Equals_AppDirectory()
    {
        var sharedPath = Path.Combine(_testDir, "shared");
        Directory.CreateDirectory(sharedPath);
        var workingRoot = new WorkingRoot(sharedPath, true, false);
        var location = MakeLocation(TargetApp.NDM, sharedPath, null);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void Allows_Sibling_Directories_Not_Nested_Either_Way()
    {
        var workingRootPath = Path.Combine(_testDir, "MDMA_Work");
        var appDir = Path.Combine(_testDir, "SomeOtherApp");
        Directory.CreateDirectory(workingRootPath);
        Directory.CreateDirectory(appDir);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);
        var location = MakeLocation(TargetApp.NDM, appDir, null);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Allows_When_Both_InstallOrConfigDir_And_MetadataDir_Are_Null()
    {
        var workingRootPath = Path.Combine(_testDir, "MDMA_Work");
        Directory.CreateDirectory(workingRootPath);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);
        var location = MakeLocation(TargetApp.NDM, null, null);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Checks_Multiple_Locations_And_Catches_Conflict_In_Any_Of_Them()
    {
        var workingRootPath = Path.Combine(_testDir, "MDMA_Work");
        Directory.CreateDirectory(workingRootPath);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);

        var safeAppDir = Path.Combine(_testDir, "SafeApp");
        Directory.CreateDirectory(safeAppDir);
        var conflictingAppDir = Path.Combine(workingRootPath, "ConflictingApp");
        Directory.CreateDirectory(conflictingAppDir);

        var locations = new[]
        {
            MakeLocation(TargetApp.NDM, safeAppDir, null),
            MakeLocation(TargetApp.JD2, conflictingAppDir, null),
        };

        var result = _provider.CheckForPathConflicts(workingRoot, locations);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void Does_Not_False_Positive_On_Paths_With_Shared_Prefix_But_Not_Nested()
    {
        // "MDMA_Work" and "MDMA_WorkOther" share a string prefix but are NOT
        // nested -- a naive StartsWith(potentialAncestor) check without the
        // trailing separator would incorrectly flag this as a conflict.
        var workingRootPath = Path.Combine(_testDir, "MDMA_Work");
        var appDir = Path.Combine(_testDir, "MDMA_WorkOther");
        Directory.CreateDirectory(workingRootPath);
        Directory.CreateDirectory(appDir);
        var workingRoot = new WorkingRoot(workingRootPath, true, false);
        var location = MakeLocation(TargetApp.NDM, appDir, null);

        var result = _provider.CheckForPathConflicts(workingRoot, new[] { location });

        Assert.That(result.IsSuccess, Is.True);
    }
}
