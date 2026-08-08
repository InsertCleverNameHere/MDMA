using System.Windows;
using System.Windows.Controls;
using Mdma.Gui.ViewModels;

namespace Mdma.Gui.Views;

public partial class ExportView : UserControl
{
    public ExportView()
    {
        InitializeComponent();
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExportViewModel vm)
        {
            var defaultFileName = vm.Task is not null
                ? $"{ExportViewModel.SanitizeFileName(vm.Task.Filename)}.mdma"
                : "package.mdma";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Destination .mdma Package File",
                Filter = "MDMA Package (*.mdma)|*.mdma|All Files (*.*)|*.*",
                FileName = defaultFileName,
            };

            if (dialog.ShowDialog() == true)
            {
                vm.DestinationPath = dialog.FileName;
            }
        }
    }
}
