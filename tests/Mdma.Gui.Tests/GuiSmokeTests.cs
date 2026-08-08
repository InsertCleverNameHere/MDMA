using Mdma.Core;
using Mdma.Core.Tests.Fixtures;
using Mdma.Gui;
using NUnit.Framework;

namespace Mdma.Gui.Tests;

public class GuiSmokeTests
{
    private sealed class TestViewModel : ObservableObject
    {
        private string _title = "";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
    }

    [Test]
    public void ObservableObject_Fires_PropertyChanged_Event()
    {
        var vm = new TestViewModel();
        string? changedProperty = null;
        vm.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

        vm.Title = "New Title";

        Assert.That(changedProperty, Is.EqualTo("Title"));
        Assert.That(vm.Title, Is.EqualTo("New Title"));
    }

    [Test]
    public void RelayCommand_Executes_Action()
    {
        bool executed = false;
        var command = new RelayCommand(() => executed = true);

        Assert.That(command.CanExecute(null), Is.True);
        command.Execute(null);

        Assert.That(executed, Is.True);
    }

    [Test]
    public void FakeConversionService_Records_Calls()
    {
        var fake = new FakeConversionService();
        var task = new DownloadTaskSummary(
            "1",
            TargetApp.NDM,
            "f.bin",
            "https://example.com/f.bin",
            100,
            50,
            "Paused",
            true
        );
        var location = new TargetAppLocation(TargetApp.NDM, "/temp", "/meta", null, true);

        fake.ExportToFile(task, location, "out.mdma");

        Assert.That(fake.ExportToFileCallCount, Is.EqualTo(1));
    }
}
