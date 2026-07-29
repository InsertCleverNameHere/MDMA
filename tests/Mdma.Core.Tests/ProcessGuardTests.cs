using Mdma.Core.Tests.Fixtures;

namespace Mdma.Core.Tests;

public class ProcessGuardTests
{
    [Test]
    public void NDM_Is_Safe_When_NeatDownloadManager_Not_Running()
    {
        var lister = new FakeProcessLister();
        var guard = new ProcessGuard(lister);

        var result = guard.IsSafeToProceed(TargetApp.NDM);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.True);
    }

    [Test]
    public void NDM_Is_Blocked_When_NeatDownloadManager_Running()
    {
        var lister = new FakeProcessLister();
        lister.RunningProcesses.Add("NeatDownloadManager.exe");
        var guard = new ProcessGuard(lister);

        var result = guard.IsSafeToProceed(TargetApp.NDM);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.False);
    }

    [Test]
    public void JD2_Is_Blocked_When_JDownloader2_Variant_Running()
    {
        var lister = new FakeProcessLister();
        lister.RunningProcesses.Add("JDownloader2.exe");
        var guard = new ProcessGuard(lister);

        var result = guard.IsSafeToProceed(TargetApp.JD2);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.False);
    }

    [Test]
    public void JD2_Is_Blocked_When_JDownloader_Legacy_Variant_Running()
    {
        var lister = new FakeProcessLister();
        lister.RunningProcesses.Add("JDownloader.exe");
        var guard = new ProcessGuard(lister);

        var result = guard.IsSafeToProceed(TargetApp.JD2);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.False);
    }

    [Test]
    public void JD2_Is_Safe_When_Neither_Variant_Running()
    {
        var lister = new FakeProcessLister();
        lister.RunningProcesses.Add("NeatDownloadManager.exe"); // unrelated process running
        var guard = new ProcessGuard(lister);

        var result = guard.IsSafeToProceed(TargetApp.JD2);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.True);
    }

    [Test]
    public void ProcessName_Matching_Is_Case_Insensitive()
    {
        var lister = new FakeProcessLister();
        lister.RunningProcesses.Add("neatdownloadmanager.exe");
        var guard = new ProcessGuard(lister);

        var result = guard.IsSafeToProceed(TargetApp.NDM);

        Assert.That(result.Value, Is.False);
    }
}
