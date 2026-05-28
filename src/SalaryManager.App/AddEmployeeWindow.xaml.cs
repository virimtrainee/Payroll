using System.Windows;
using System.Windows.Controls;
using SalaryManager.App.Helpers;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App;

public partial class AddEmployeeWindow : UserControl
{
    public AddEmployeeWindow(AddEmployeeViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddEmployeeViewModel vm)
        {
            var validation = vm.Validate();
            if (!validation.IsValid)
            {
                ValidationErrorText.Text = validation.ToDisplayString();
                ValidationErrorText.Visibility = Visibility.Visible;
                return;
            }
        }

        DialogWindowCloser.Close(this, true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogWindowCloser.Close(this, false);
    }
}
