using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace SalaryManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly System.IServiceProvider _sp;

    public DashboardViewModel   DashboardVm   { get; }
    public SalarySheetViewModel SalarySheetVm { get; }
    public AdvancesViewModel    AdvancesVm    { get; }
    public ReportsViewModel     ReportsVm     { get; }

    public MainViewModel(System.IServiceProvider sp)
    {
        _sp = sp;
        DashboardVm   = sp.GetRequiredService<DashboardViewModel>();
        SalarySheetVm = sp.GetRequiredService<SalarySheetViewModel>();
        AdvancesVm    = sp.GetRequiredService<AdvancesViewModel>();
        ReportsVm     = sp.GetRequiredService<ReportsViewModel>();
    }

    [RelayCommand]
    private void OpenEmployees()
    {
        var win = _sp.GetRequiredService<EmployeesWindow>();
        win.Owner = Application.Current.MainWindow;
        win.ShowDialog();
    }
}
