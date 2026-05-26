using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;

namespace SalaryManager.App.ViewModels;

public partial class AttendanceRow : ObservableObject
{
    public int EmployeeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal BaseSalary { get; init; }
    public bool UsesTds => BaseSalary > 25000m;
    public bool UsesEsicPf => !UsesTds;

    [ObservableProperty] private int daysAbsent;
    [ObservableProperty] private decimal esicDeduction;
    [ObservableProperty] private decimal pfDeduction;
    [ObservableProperty] private decimal tdsDeduction;
}

public partial class AttendanceViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly DialogService _dialogs;

    public List<MonthOption> Months => Helpers.Months.All;
    public List<int> Years => Helpers.Months.Years();

    [ObservableProperty] private MonthOption selectedMonth = Helpers.Months.All[DateTime.Now.Month - 1];
    [ObservableProperty] private int selectedYear = DateTime.Now.Year;
    [ObservableProperty] private int daysInMonth;
    [ObservableProperty] private GroupFilterOptionVm? selectedGroupFilter;
    private bool _updatingGroupFilter;

    public RangeObservableCollection<AttendanceRow> Rows { get; } = new();
    public RangeObservableCollection<GroupFilterOptionVm> GroupFilterOptions { get; } = new();

    public AttendanceViewModel(IDbContextFactory<AppDbContext> dbf,
                               DatabaseInitializer dbInit,
                               DialogService dialogs)
    {
        _dbf = dbf;
        _dbInit = dbInit;
        _dialogs = dialogs;
    }

    partial void OnSelectedMonthChanged(MonthOption value) => _ = LoadAsync();
    partial void OnSelectedYearChanged(int value) => _ = LoadAsync();
    partial void OnSelectedGroupFilterChanged(GroupFilterOptionVm? value)
    {
        if (!_updatingGroupFilter) _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _dbInit.ReadyTask;

        DaysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth.Number);
        using var db = await _dbf.CreateDbContextAsync();
        await LoadGroupFiltersAsync(db);

        var employeeQuery = db.Employees.AsNoTracking().Where(e => e.IsActive);
        if (SelectedGroupFilter?.Id is int groupId)
            employeeQuery = employeeQuery.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

        var employees = await employeeQuery.OrderBy(e => e.Name).ToListAsync();

        var existing = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Year == SelectedYear && a.Month == SelectedMonth.Number)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);

        var newRows = new List<AttendanceRow>(employees.Count);
        foreach (var e in employees)
        {
            var rec = existing.GetValueOrDefault(e.Id);
            newRows.Add(new AttendanceRow
            {
                EmployeeId = e.Id,
                Name = e.Name,
                BaseSalary = e.BaseSalary,
                DaysAbsent = rec?.DaysAbsent ?? 0,
                EsicDeduction = e.BaseSalary <= 25000m ? rec?.EsicDeduction ?? 0m : 0m,
                PfDeduction = e.BaseSalary <= 25000m ? rec?.PfDeduction ?? 0m : 0m,
                TdsDeduction = e.BaseSalary > 25000m ? rec?.TdsDeduction ?? 0m : 0m,
            });
        }
        Rows.ReplaceAll(newRows);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();

        var empIds = Rows.Select(r => r.EmployeeId).ToList();
        var existing = await db.AttendanceRecords
            .Where(a => a.Year == SelectedYear
                     && a.Month == SelectedMonth.Number
                     && empIds.Contains(a.EmployeeId))
            .ToDictionaryAsync(a => a.EmployeeId);

        foreach (var r in Rows)
        {
            if (existing.TryGetValue(r.EmployeeId, out var rec))
            {
                rec.DaysAbsent = Math.Clamp(r.DaysAbsent, 0, DaysInMonth);
                rec.EsicDeduction = r.UsesEsicPf ? Math.Max(0, r.EsicDeduction) : 0m;
                rec.PfDeduction = r.UsesEsicPf ? Math.Max(0, r.PfDeduction) : 0m;
                rec.TdsDeduction = r.UsesTds ? Math.Max(0, r.TdsDeduction) : 0m;
            }
            else
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    EmployeeId = r.EmployeeId,
                    Year = SelectedYear,
                    Month = SelectedMonth.Number,
                    DaysAbsent = Math.Clamp(r.DaysAbsent, 0, DaysInMonth),
                    EsicDeduction = r.UsesEsicPf ? Math.Max(0, r.EsicDeduction) : 0m,
                    PfDeduction = r.UsesEsicPf ? Math.Max(0, r.PfDeduction) : 0m,
                    TdsDeduction = r.UsesTds ? Math.Max(0, r.TdsDeduction) : 0m,
                });
            }
        }

        await db.SaveChangesAsync();
        _dialogs.Info("Attendance saved.");
    }

    private async Task LoadGroupFiltersAsync(AppDbContext db)
    {
        var selectedId = SelectedGroupFilter?.Id;
        var groups = await db.EmployeeGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupFilterOptionVm(g.Id, g.Name))
            .ToListAsync();

        var options = new List<GroupFilterOptionVm> { new(null, "All Groups") };
        options.AddRange(groups);

        _updatingGroupFilter = true;
        try
        {
            GroupFilterOptions.ReplaceAll(options);
            SelectedGroupFilter = options.FirstOrDefault(g => g.Id == selectedId) ?? options[0];
        }
        finally
        {
            _updatingGroupFilter = false;
        }
    }
}
