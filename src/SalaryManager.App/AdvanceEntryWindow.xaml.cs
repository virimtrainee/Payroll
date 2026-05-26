using System.Windows;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App;

public partial class AdvanceEntryWindow : Window
{
    public AdvanceEntryWindow(AdvanceEntryEditVm vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
