using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace SalaryManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly System.IServiceProvider _sp;

    [ObservableProperty] private int selectedPageIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    [NotifyPropertyChangedFor(nameof(SidebarTextVisibility))]
    [NotifyPropertyChangedFor(nameof(SidebarToggleGlyph))]
    [NotifyPropertyChangedFor(nameof(SidebarIconMargin))]
    [NotifyPropertyChangedFor(nameof(SidebarItemAlignment))]
    private bool isSidebarCollapsed;

    public GridLength SidebarWidth => new(IsSidebarCollapsed ? 76 : 252);
    public Visibility SidebarTextVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
    public string SidebarToggleGlyph => IsSidebarCollapsed ? "›" : "‹";
    public Thickness SidebarIconMargin => IsSidebarCollapsed ? new Thickness(0) : new Thickness(0, 0, 12, 0);
    public HorizontalAlignment SidebarItemAlignment => IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;

    public DashboardViewModel DashboardVm { get; }
    public SalarySheetViewModel SalarySheetVm { get; }
    public AdvancesViewModel AdvancesVm { get; }
    public ReportsViewModel ReportsVm { get; }
    public EmployeesViewModel EmployeesVm { get; }

    public MainViewModel(System.IServiceProvider sp)
    {
        _sp = sp;
        DashboardVm = sp.GetRequiredService<DashboardViewModel>();
        SalarySheetVm = sp.GetRequiredService<SalarySheetViewModel>();
        AdvancesVm = sp.GetRequiredService<AdvancesViewModel>();
        ReportsVm = sp.GetRequiredService<ReportsViewModel>();
        EmployeesVm = sp.GetRequiredService<EmployeesViewModel>();
        EmployeesVm.EmployeePermanentlyDeleted += (_, _) => ReloadPayrollViews();
    }

    [RelayCommand]
    private void ShowReports() => SelectedPageIndex = 3;

    [RelayCommand]
    private void ShowAdvances() => SelectedPageIndex = 2;

    [RelayCommand]
    private void ShowEmployees() => SelectedPageIndex = 4;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void OpenEmployees()
    {
        var win = _sp.GetRequiredService<EmployeesWindow>();
        win.Owner = Application.Current.MainWindow;
        win.ShowDialog();
        DashboardVm.LoadCommand.Execute(null);
        SalarySheetVm.LoadCommand.Execute(null);
        AdvancesVm.LoadCommand.Execute(null);
        ReportsVm.LoadCommand.Execute(null);
    }

    private void ReloadPayrollViews()
    {
        DashboardVm.LoadCommand.Execute(null);
        SalarySheetVm.LoadCommand.Execute(null);
        AdvancesVm.LoadCommand.Execute(null);
        ReportsVm.LoadCommand.Execute(null);
    }
}
