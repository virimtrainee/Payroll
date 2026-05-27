using System.Windows;
using System.Windows.Controls;

namespace SalaryManager.App.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView() => InitializeComponent();

    private void ManageGroups_Click(object sender, RoutedEventArgs e)
    {
        EmployeeTabs.SelectedIndex = 1;
    }
}
