using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class SpaceCheckerTests
{
    [Test]
    public void Sufficient_Space_Returns_Ok()
    {
        var disk = new FakeDiskSpaceSource { FreeBytes = 1_000_000 };
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: 500_000, isDestination: false);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Insufficient_Space_Source_Returns_Correct_ErrorCode()
    {
        var disk = new FakeDiskSpaceSource { FreeBytes = 100 };
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: 1_000, isDestination: false);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InsufficientDiskSpaceSource));
    }

    [Test]
    public void Insufficient_Space_Destination_Returns_Correct_ErrorCode()
    {
        var disk = new FakeDiskSpaceSource { FreeBytes = 100 };
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: 1_000, isDestination: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(MdmaErrorCode.InsufficientDiskSpaceDestination));
    }

    [Test]
    public void Margin_Is_Applied_Exactly_Enough_For_Raw_Bytes_But_Not_Margin_Fails()
    {
        // requiredBytes = 1000 -> with 15% margin needs 1150 available.
        // Give exactly 1000 free: enough for raw bytes, not enough with margin.
        var disk = new FakeDiskSpaceSource { FreeBytes = 1_000 };
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: 1_000, isDestination: false);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void Margin_Is_Applied_Exactly_At_Margin_Boundary_Succeeds()
    {
        long required = 1_000;
        long withMargin = (long)Math.Ceiling(required * (1.0 + SpaceChecker.SafetyMarginFraction));
        var disk = new FakeDiskSpaceSource { FreeBytes = withMargin };
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: required, isDestination: false);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Shortfall_Details_Contains_Human_Readable_Numbers()
    {
        var disk = new FakeDiskSpaceSource { FreeBytes = 1_000_000_000 }; // ~0.93 GB
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: 5_000_000_000, isDestination: true);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Details, Does.Contain("GB"));
        Assert.That(result.Error!.Details, Does.Contain("Short by"));
    }

    [Test]
    public void Different_Paths_Use_Correct_FreeBytes_Via_PerPath_Fake()
    {
        var disk = new FakeDiskSpaceSource();
        disk.FreeBytesByPath[@"C:\full-drive"] = 10;
        disk.FreeBytesByPath[@"D:\roomy-drive"] = 1_000_000_000;
        var checker = new SpaceChecker(disk);

        var fullResult = checker.HasSufficientSpace(@"C:\full-drive", requiredBytes: 1_000, isDestination: false);
        var roomyResult = checker.HasSufficientSpace(@"D:\roomy-drive", requiredBytes: 1_000, isDestination: false);

        Assert.That(fullResult.IsSuccess, Is.False);
        Assert.That(roomyResult.IsSuccess, Is.True);
    }

    [Test]
    public void Negative_RequiredBytes_Returns_Error_Rather_Than_Throwing()
    {
        var disk = new FakeDiskSpaceSource { FreeBytes = 1_000 };
        var checker = new SpaceChecker(disk);

        var result = checker.HasSufficientSpace(@"C:\somewhere", requiredBytes: -1, isDestination: false);

        Assert.That(result.IsSuccess, Is.False);
    }
}
