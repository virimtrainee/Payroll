using System;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace SalaryManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly System.IServiceProvider _sp;
    private const int DashboardPageIndex = 0;
    private const int SalarySheetPageIndex = 1;
    private const int AdvancesPageIndex = 2;
    private const int ReportsPageIndex = 3;
    private const int EmployeesPageIndex = 4;
    private readonly bool[] _payrollPageDirty = new bool[EmployeesPageIndex];
    private bool _employeesDirty = true;

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
        EmployeesVm.EmployeeDataChanged += (_, _) => MarkPayrollViewsDirty();
    }

    [RelayCommand]
    private void ShowReports()
    {
        SelectedPageIndex = ReportsPageIndex;
        ReloadDirtyPayrollView(ReportsPageIndex);
    }

    [RelayCommand]
    private void ShowAdvances()
    {
        SelectedPageIndex = AdvancesPageIndex;
        ReloadDirtyPayrollView(AdvancesPageIndex);
    }

    [RelayCommand]
    private void ShowEmployees()
    {
        SelectedPageIndex = EmployeesPageIndex;
        LoadEmployeesIfNeeded();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void OpenEmployees()
    {
        var win = _sp.GetRequiredService<EmployeesWindow>();
        win.Owner = Application.Current.MainWindow;
        if (win.DataContext is EmployeesViewModel employeesVm)
            ExecuteIfPossible(employeesVm.LoadCommand);

        win.ShowDialog();
        _employeesDirty = true;
        if (SelectedPageIndex == EmployeesPageIndex)
            LoadEmployeesIfNeeded();
        MarkPayrollViewsDirty();
    }

    partial void OnSelectedPageIndexChanged(int value)
    {
        if (value == EmployeesPageIndex)
        {
            LoadEmployeesIfNeeded();
            return;
        }

        ReloadDirtyPayrollView(value);
    }

    private void MarkPayrollViewsDirty()
    {
        for (var i = 0; i < _payrollPageDirty.Length; i++)
            _payrollPageDirty[i] = true;

        ReloadDirtyPayrollView(SelectedPageIndex);
    }

    private void ReloadDirtyPayrollView(int pageIndex)
    {
        if (!IsPayrollPageIndex(pageIndex) || !_payrollPageDirty[pageIndex])
            return;

        var command = LoadCommandFor(pageIndex);
        if (!ExecuteIfPossible(command))
            return;

        _payrollPageDirty[pageIndex] = false;
    }

    private void LoadEmployeesIfNeeded()
    {
        if (!_employeesDirty)
            return;

        if (!ExecuteIfPossible(EmployeesVm.LoadCommand))
            return;

        _employeesDirty = false;
    }

    private ICommand LoadCommandFor(int pageIndex) => pageIndex switch
    {
        DashboardPageIndex => DashboardVm.LoadCommand,
        SalarySheetPageIndex => SalarySheetVm.LoadCommand,
        AdvancesPageIndex => AdvancesVm.LoadCommand,
        ReportsPageIndex => ReportsVm.LoadCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, null)
    };

    private static bool IsPayrollPageIndex(int pageIndex)
        => pageIndex is >= DashboardPageIndex and < EmployeesPageIndex;

    private static bool ExecuteIfPossible(ICommand command)
    {
        if (!command.CanExecute(null))
            return false;

        command.Execute(null);
        return true;
    }
}
