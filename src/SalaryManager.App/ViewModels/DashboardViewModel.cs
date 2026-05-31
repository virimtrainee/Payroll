using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly MonthlyPayrollService _monthlyPayroll;

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
                              DialogService dialogs,
                              MonthlyPayrollService monthlyPayroll)
    {
        _dbf = dbf; _dbInit = dbInit; _backup = backup; _dialogs = dialogs; _monthlyPayroll = monthlyPayroll;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var totalStopwatch = Stopwatch.StartNew();
        TimeSpan payrollElapsed = TimeSpan.Zero;
        TimeSpan databaseElapsed = TimeSpan.Zero;
        var payrollRowCount = 0;
        IsLoading = true;
        try
        {
            await _dbInit.ReadyTask;

            var now = DateTime.Now;
            CurrentPeriod = now.ToString("MMMM yyyy");
            var currentMonthStart = new DateTime(now.Year, now.Month, 1);
            var nextMonthStart = currentMonthStart.AddMonths(1);
            var stepStopwatch = Stopwatch.StartNew();
            var payroll = await _monthlyPayroll.LoadAsync(new MonthlyPayrollRequest(now.Year, now.Month));
            payrollElapsed = stepStopwatch.Elapsed;
            payrollRowCount = payroll.Rows.Count;
            using var db = await _dbf.CreateDbContextAsync();

            stepStopwatch.Restart();
            TotalEmployees = await db.Employees.CountAsync();
            var activeEmployeeIds = payroll.Rows.Select(r => r.EmployeeId).ToList();
            ActiveEmployees = payroll.Rows.Count;
            GrossPayroll = payroll.Rows.Sum(r => Math.Max(0m, r.SalaryPaid));
            PayablePayroll = payroll.Rows
                .Where(r => r.SalaryPaid >= 0m && r.Breakdown is not null)
                .Sum(r => Math.Max(0m, r.NetSalary));
            var advanceSummary = await db.Advances.AsNoTracking().SumBalanceSummaryAsync();
            OutstandingAdvances = advanceSummary.Outstanding;
            EmployeesWithPendingAdvances = advanceSummary.EmployeesWithPendingAdvances;

            AttendanceSavedThisMonth = await db.AttendanceRecords.AsNoTracking()
                .Where(a => activeEmployeeIds.Contains(a.EmployeeId)
                         && a.Year == now.Year
                         && a.Month == now.Month)
                .Select(a => a.EmployeeId)
                .Distinct()
                .CountAsync();

            var currentMonthRevisions = await db.SalaryRevisions.AsNoTracking()
                .Where(r => r.ChangedAt >= currentMonthStart && r.ChangedAt < nextMonthStart)
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
            databaseElapsed = stepStopwatch.Elapsed;

            PerformanceTrace.DashboardLoad(
                now.Year,
                now.Month,
                payrollRowCount,
                totalStopwatch.Elapsed,
                payrollElapsed,
                databaseElapsed);
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
}
