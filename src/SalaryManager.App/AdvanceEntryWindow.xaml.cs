using System.Windows;
using System.Windows.Controls;
using SalaryManager.App.Helpers;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App;

public partial class AdvanceEntryWindow : UserControl
{
    public AdvanceEntryWindow(AdvanceEntryEditVm vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogWindowCloser.Close(this, true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogWindowCloser.Close(this, false);
    }
}
