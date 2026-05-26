using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using SalaryManager.Data.Validation;

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
    private int _loadVersion;

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

    partial void OnSelectedMonthChanged(MonthOption value) => LoadCommand.Execute(null);
    partial void OnSelectedYearChanged(int value) => LoadCommand.Execute(null);
    partial void OnSelectedGroupFilterChanged(GroupFilterOptionVm? value)
    {
        if (!_updatingGroupFilter) LoadCommand.Execute(null);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        try
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

            if (version != _loadVersion) return;
            Rows.ReplaceAll(newRows);
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();
        var validation = ValidateRows(Rows, SelectedYear, SelectedMonth.Number);
        if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

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
                rec.DaysAbsent = r.DaysAbsent;
                rec.EsicDeduction = r.UsesEsicPf ? r.EsicDeduction : 0m;
                rec.PfDeduction = r.UsesEsicPf ? r.PfDeduction : 0m;
                rec.TdsDeduction = r.UsesTds ? r.TdsDeduction : 0m;
            }
            else
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    EmployeeId = r.EmployeeId,
                    Year = SelectedYear,
                    Month = SelectedMonth.Number,
                    DaysAbsent = r.DaysAbsent,
                    EsicDeduction = r.UsesEsicPf ? r.EsicDeduction : 0m,
                    PfDeduction = r.UsesEsicPf ? r.PfDeduction : 0m,
                    TdsDeduction = r.UsesTds ? r.TdsDeduction : 0m,
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

    private static ValidationResult ValidateRows(IEnumerable<AttendanceRow> rows, int year, int month)
    {
        var issues = rows
            .SelectMany(row => PayrollValidator.Validate(new PayrollValidationInput(
                row.Name,
                row.BaseSalary,
                year,
                month,
                row.DaysAbsent,
                row.EsicDeduction,
                row.PfDeduction,
                row.TdsDeduction,
                0m,
                0m)).Issues)
            .ToList();
        return new ValidationResult(issues);
    }
}
