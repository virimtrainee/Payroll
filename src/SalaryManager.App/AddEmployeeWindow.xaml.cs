using System.Windows;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App;

public partial class AddEmployeeWindow : Window
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
                MessageBox.Show(
                    this,
                    validation.ToDisplayString(),
                    "Invalid employee",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
