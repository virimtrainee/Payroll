using System.Windows;
using SalaryManager.App.Helpers;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App;

public partial class EmployeesWindow : Window
{
    public EmployeesWindow(EmployeesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            vm.OwnerWindow = this;
            UiCommandScheduler.ExecuteDeferredIfPossible(vm.LoadCommand, dispatcherSource: this);
        };
    }
}
