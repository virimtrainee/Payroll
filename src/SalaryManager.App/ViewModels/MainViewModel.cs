using System;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SalaryManager.App.Helpers;

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
    private readonly bool[] _payrollPageReloadScheduled = new bool[EmployeesPageIndex];
    private bool _employeesDirty = true;
    private bool _employeeReloadScheduled;

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
        SelectPayrollPage(ReportsPageIndex);
    }

    [RelayCommand]
    private void ShowAdvances()
    {
        SelectPayrollPage(AdvancesPageIndex);
    }

    [RelayCommand]
    private void ShowEmployees()
    {
        if (SelectedPageIndex == EmployeesPageIndex)
            LoadEmployeesIfNeeded();
        else
            SelectedPageIndex = EmployeesPageIndex;
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void OpenEmployees()
    {
        var win = _sp.GetRequiredService<EmployeesWindow>();
        if (Application.Current?.MainWindow is { } owner)
            win.Owner = owner;

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
        if (!IsPayrollPageIndex(pageIndex) || !_payrollPageDirty[pageIndex] || _payrollPageReloadScheduled[pageIndex])
            return;

        var command = LoadCommandFor(pageIndex);
        _payrollPageReloadScheduled[pageIndex] = true;
        if (!UiCommandScheduler.ExecuteDeferredIfPossible(
                command,
                onExecuted: () =>
                {
                    _payrollPageDirty[pageIndex] = false;
                    _payrollPageReloadScheduled[pageIndex] = false;
                },
                onSkipped: () => _payrollPageReloadScheduled[pageIndex] = false))
        {
            _payrollPageReloadScheduled[pageIndex] = false;
        }
    }

    private void LoadEmployeesIfNeeded()
    {
        if (!_employeesDirty || _employeeReloadScheduled)
            return;

        _employeeReloadScheduled = true;
        if (!UiCommandScheduler.ExecuteDeferredIfPossible(
                EmployeesVm.LoadCommand,
                onExecuted: () =>
                {
                    _employeesDirty = false;
                    _employeeReloadScheduled = false;
                },
                onSkipped: () => _employeeReloadScheduled = false))
        {
            _employeeReloadScheduled = false;
        }
    }

    private void SelectPayrollPage(int pageIndex)
    {
        if (SelectedPageIndex == pageIndex)
            ReloadDirtyPayrollView(pageIndex);
        else
            SelectedPageIndex = pageIndex;
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
}
