using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.App.ViewModels;

public record RecentAdvanceVm(string EmployeeName, decimal Amount, AdvanceEntryType EntryType, DateTime Date);
public record RevisionVm(string EmployeeName, decimal OldSalary, decimal NewSalary, DateTime ChangedAt);

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly BackupService _backup;
    private readonly DialogService _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAttendanceComplete))]
    [NotifyPropertyChangedFor(nameof(AttendanceStatusText))]
    [NotifyPropertyChangedFor(nameof(AttendanceStatusBackground))]
    [NotifyPropertyChangedFor(nameof(AttendanceStatusForeground))]
    [NotifyPropertyChangedFor(nameof(AttendanceRemainingCount))]
    [NotifyPropertyChangedFor(nameof(PayrollBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PayrollBannerSubtitle))]
    private int activeEmployees;
    [ObservableProperty] private decimal grossPayroll;
    [ObservableProperty] private decimal payablePayroll;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvanceStatusText))]
    [NotifyPropertyChangedFor(nameof(AdvanceStatusBackground))]
    [NotifyPropertyChangedFor(nameof(AdvanceStatusForeground))]
    private decimal outstandingAdvances;
    [ObservableProperty] private int employeesWithPendingAdvances;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAttendanceComplete))]
    [NotifyPropertyChangedFor(nameof(AttendanceStatusText))]
    [NotifyPropertyChangedFor(nameof(AttendanceStatusBackground))]
    [NotifyPropertyChangedFor(nameof(AttendanceStatusForeground))]
    [NotifyPropertyChangedFor(nameof(AttendanceRemainingCount))]
    [NotifyPropertyChangedFor(nameof(PayrollBannerTitle))]
    [NotifyPropertyChangedFor(nameof(PayrollBannerSubtitle))]
    private int attendanceSavedThisMonth;
    [ObservableProperty] private int totalEmployees;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SalaryReviewStatusText))]
    [NotifyPropertyChangedFor(nameof(SalaryReviewStatusBackground))]
    [NotifyPropertyChangedFor(nameof(SalaryReviewStatusForeground))]
    private int salaryChangesCount;
    [ObservableProperty] private decimal totalRevisionIncrease;
    [ObservableProperty] private string currentPeriod = DateTime.Now.ToString("MMMM yyyy");
    [ObservableProperty] private bool isLoading;

    public ObservableCollection<RecentAdvanceVm> RecentAdvances { get; } = new();
    public ObservableCollection<RevisionVm> RecentRevisions { get; } = new();

    private static readonly string CashGroupNormalizedName = GroupNameNormalizer.Normalize("Cash");

    public bool IsAttendanceComplete => ActiveEmployees > 0 && AttendanceSavedThisMonth >= ActiveEmployees;
    public int AttendanceRemainingCount => Math.Max(0, ActiveEmployees - AttendanceSavedThisMonth);
    public string AttendanceStatusText => IsAttendanceComplete ? "Completed" : "Pending";
    public string AttendanceStatusBackground => IsAttendanceComplete ? "#DCFCE7" : "#FFEDD5";
    public string AttendanceStatusForeground => IsAttendanceComplete ? "#166534" : "#EA580C";
    public string SalaryReviewStatusText => SalaryChangesCount > 0 ? "Review" : "Completed";
    public string SalaryReviewStatusBackground => SalaryChangesCount > 0 ? "#F3E8FF" : "#DCFCE7";
    public string SalaryReviewStatusForeground => SalaryChangesCount > 0 ? "#7C3AED" : "#166534";
    public string AdvanceStatusText => OutstandingAdvances != 0m ? "Pending" : "Clear";
    public string AdvanceStatusBackground => OutstandingAdvances != 0m ? "#FFEDD5" : "#DCFCE7";
    public string AdvanceStatusForeground => OutstandingAdvances != 0m ? "#EA580C" : "#166534";
    public string PayrollBannerTitle => IsAttendanceComplete
        ? "You're all set! Attendance is complete and payroll is ready."
        : "Attendance is still pending.";
    public string PayrollBannerSubtitle => IsAttendanceComplete
        ? "Review salary changes and generate payroll when ready."
        : $"{AttendanceRemainingCount} employee{(AttendanceRemainingCount == 1 ? "" : "s")} need attendance before payroll is ready.";

    public DashboardViewModel(IDbContextFactory<AppDbContext> dbf,
                              DatabaseInitializer dbInit,
                              BackupService backup,
                              DialogService dialogs)
    {
        _dbf = dbf; _dbInit = dbInit; _backup = backup; _dialogs = dialogs;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _dbInit.ReadyTask;

            CurrentPeriod = DateTime.Now.ToString("MMMM yyyy");
            using var db = await _dbf.CreateDbContextAsync();
            var now = DateTime.Now;

            TotalEmployees = await db.Employees.CountAsync();
            var activeEmployees = await db.Employees.AsNoTracking()
                .Include(e => e.GroupMemberships)
                    .ThenInclude(m => m.EmployeeGroup)
                .Where(e => e.IsActive)
                .OrderBy(e => e.Name)
                .ToListAsync();
            var activeEmployeeIds = activeEmployees.Select(e => e.Id).ToList();
            ActiveEmployees = activeEmployees.Count;
            GrossPayroll = await CalculateGrossPayrollAsync(db, activeEmployees, now.Year, now.Month);
            PayablePayroll = await CalculatePayablePayrollAsync(db, activeEmployees, now.Year, now.Month);
            var balancesByEmployee = await db.Advances.AsNoTracking().SumBalancesByEmployeeAsync();
            OutstandingAdvances = balancesByEmployee.Values.Sum();
            EmployeesWithPendingAdvances = balancesByEmployee.Count(kv => kv.Value != 0m);

            AttendanceSavedThisMonth = await db.AttendanceRecords.AsNoTracking()
                .Where(a => activeEmployeeIds.Contains(a.EmployeeId)
                         && a.Year == now.Year
                         && a.Month == now.Month)
                .Select(a => a.EmployeeId)
                .Distinct()
                .CountAsync();

            var currentMonthRevisions = await db.SalaryRevisions.AsNoTracking()
                .Where(r => r.ChangedAt.Year == now.Year && r.ChangedAt.Month == now.Month)
                .ToListAsync();
            SalaryChangesCount = currentMonthRevisions.Count;
            TotalRevisionIncrease = currentMonthRevisions
                .Where(r => r.NewSalary > r.OldSalary)
                .Sum(r => r.NewSalary - r.OldSalary);

            var recentAdv = await db.Advances.AsNoTracking()
                .Include(a => a.Employee)
                .OrderByDescending(a => a.Date).ThenByDescending(a => a.Id)
                .Take(7)
                .ToListAsync();
            RecentAdvances.Clear();
            foreach (var a in recentAdv)
                RecentAdvances.Add(new RecentAdvanceVm(a.Employee?.Name ?? "", a.Amount, a.EntryType, a.Date));

            var recentRev = await db.SalaryRevisions.AsNoTracking()
                .Include(r => r.Employee)
                .OrderByDescending(r => r.ChangedAt)
                .Take(8)
                .ToListAsync();
            RecentRevisions.Clear();
            foreach (var r in recentRev)
                RecentRevisions.Add(new RevisionVm(r.Employee.Name, r.OldSalary, r.NewSalary, r.ChangedAt));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task BackupDatabase()
    {
        try
        {
            var defaultName = $"salary-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var path = _dialogs.AskSavePath("SQLite Database (*.db)|*.db", defaultName);
            if (path is null) return;
            _backup.Backup(path);
            await _dialogs.InfoAsync($"Backup saved to:\n{path}");
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task RestoreDatabase()
    {
        if (!await _dialogs.ConfirmAsync(
            "Restoring will replace the current database with the selected backup.\n" +
            "The app will close — restart it after.\n\nContinue?"))
            return;
        try
        {
            var path = _dialogs.AskOpenPath("SQLite Database (*.db)|*.db");
            if (path is null) return;
            _backup.Restore(path);
            await _dialogs.InfoAsync("Database restored. Please restart the application.");
            Application.Current.Shutdown();
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    private static async Task<decimal> CalculatePayablePayrollAsync(
        AppDbContext db,
        IReadOnlyList<Employee> activeEmployees,
        int year,
        int month)
    {
        if (activeEmployees.Count == 0)
            return 0m;

        var employeeIds = activeEmployees.Select(e => e.Id).ToList();
        var sourceKey = AdvanceSourceKeys.Salary(year, month);
        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                     && a.Year == year
                     && a.Month == month)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);
        var advanceDeductions = await db.Advances.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                     && a.SourceKey == sourceKey
                     && a.EntryType == AdvanceEntryType.Deducted)
            .GroupBy(a => a.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Amount = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Amount);

        decimal total = 0m;
        foreach (var employee in activeEmployees)
        {
            attendance.TryGetValue(employee.Id, out var record);
            var effectiveBaseSalary = EffectiveBaseSalary(employee, record);
            if (effectiveBaseSalary < 0m)
                continue;

            var daysAbsent = record?.DaysAbsent ?? 0;
            var esic = UsesEsicPf(employee, effectiveBaseSalary) ? record?.EsicDeduction ?? 0m : 0m;
            var pf = UsesEsicPf(employee, effectiveBaseSalary) ? record?.PfDeduction ?? 0m : 0m;
            var tds = UsesTds(employee, effectiveBaseSalary) ? record?.TdsDeduction ?? 0m : 0m;
            var advanceDeduction = advanceDeductions.GetValueOrDefault(employee.Id, 0m);
            var breakdown = SalaryCalculator.Compute(effectiveBaseSalary, year, month, daysAbsent, esic, pf, tds);
            var historicalNetOverride = EffectiveHistoricalNetSalaryOverride(record);
            total += Math.Max(0m, historicalNetOverride ?? breakdown.NetSalary - advanceDeduction);
        }

        return total;
    }

    private static async Task<decimal> CalculateGrossPayrollAsync(
        AppDbContext db,
        IReadOnlyList<Employee> activeEmployees,
        int year,
        int month)
    {
        if (activeEmployees.Count == 0)
            return 0m;

        var employeeIds = activeEmployees.Select(e => e.Id).ToList();
        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                     && a.Year == year
                     && a.Month == month)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);

        return activeEmployees.Sum(employee =>
        {
            attendance.TryGetValue(employee.Id, out var record);
            return Math.Max(0m, EffectiveBaseSalary(employee, record));
        });
    }

    private static decimal EffectiveBaseSalary(Employee employee, AttendanceRecord? attendance)
        => attendance?.BaseSalaryOverride ?? employee.BaseSalary;

    private static decimal? EffectiveHistoricalNetSalaryOverride(AttendanceRecord? attendance)
        => attendance?.BaseSalaryOverride is null ? attendance?.NetSalaryOverride : null;

    private static bool UsesEsicPf(Employee employee, decimal baseSalary)
        => !HasCashDeductionsDisabled(employee) && baseSalary <= 25000m;

    private static bool UsesTds(Employee employee, decimal baseSalary)
        => !HasCashDeductionsDisabled(employee) && baseSalary > 25000m;

    private static bool HasCashDeductionsDisabled(Employee employee)
        => employee.PaymentMode == PaymentMode.Cash
        || employee.GroupMemberships.Any(m =>
            m.EmployeeGroup is not null
            && GroupNameNormalizer.Normalize(m.EmployeeGroup.Name) == CashGroupNormalizedName);
}
