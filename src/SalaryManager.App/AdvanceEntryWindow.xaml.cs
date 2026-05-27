using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
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
        DialogHost.CloseDialogCommand.Execute(true, this);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogHost.CloseDialogCommand.Execute(false, this);
    }
}
