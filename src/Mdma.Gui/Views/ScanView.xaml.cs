using System.Windows;
using System.Windows.Controls;
using Mdma.Core;
using Mdma.Gui.ViewModels;

namespace Mdma.Gui.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
    }

    private void BrowseNdm_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(TargetApp.NDM);
    }

    private void BrowseJd2_Click(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(TargetApp.JD2);
    }

    private void BrowseForFolder(TargetApp app)
    {
        if (DataContext is ScanViewModel vm)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = $"Select {app} Configuration/Data Folder",
            };

            if (dialog.ShowDialog() == true)
            {
                vm.SetManualPath(app, dialog.FolderName);
            }
        }
    }
}
