using System.Windows;
using System.Windows.Controls;
using Mdma.Gui.ViewModels;

namespace Mdma.Gui.Views;

public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
    }

    private void BrowsePackage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImportViewModel vm)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select .mdma Package File",
                Filter = "MDMA Package (*.mdma)|*.mdma|All Files (*.*)|*.*",
            };

            if (dialog.ShowDialog() == true)
            {
                vm.PackageFilePath = dialog.FileName;
            }
        }
    }
}
