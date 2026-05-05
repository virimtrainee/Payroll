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

    [ObservableProperty] private int daysAbsent;
    [ObservableProperty] private decimal esicDeduction;
    [ObservableProperty] private decimal pfDeduction;
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

    public RangeObservableCollection<AttendanceRow> Rows { get; } = new();

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

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _dbInit.ReadyTask;

        DaysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth.Number);
        using var db = await _dbf.CreateDbContextAsync();

        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .ToListAsync();

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
                EsicDeduction = rec?.EsicDeduction ?? 0m,
                PfDeduction = rec?.PfDeduction ?? 0m,
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
                rec.EsicDeduction = Math.Max(0, r.EsicDeduction);
                rec.PfDeduction = Math.Max(0, r.PfDeduction);
            }
            else
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    EmployeeId = r.EmployeeId,
                    Year = SelectedYear,
                    Month = SelectedMonth.Number,
                    DaysAbsent = Math.Clamp(r.DaysAbsent, 0, DaysInMonth),
                    EsicDeduction = Math.Max(0, r.EsicDeduction),
                    PfDeduction = Math.Max(0, r.PfDeduction),
                });
            }
        }

        await db.SaveChangesAsync();
        _dialogs.Info("Attendance saved.");
    }
}
