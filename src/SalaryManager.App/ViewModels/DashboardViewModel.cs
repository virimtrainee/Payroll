using System;
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

    [ObservableProperty] private int    activeEmployees;
    [ObservableProperty] private decimal grossPayroll;
    [ObservableProperty] private decimal outstandingAdvances;
    [ObservableProperty] private int    attendanceSavedThisMonth;
    [ObservableProperty] private int    totalEmployees;
    [ObservableProperty] private string currentPeriod = DateTime.Now.ToString("MMMM yyyy");
    [ObservableProperty] private bool   isLoading;

    public ObservableCollection<RecentAdvanceVm> RecentAdvances  { get; } = new();
    public ObservableCollection<RevisionVm>      RecentRevisions { get; } = new();

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
            using var db  = await _dbf.CreateDbContextAsync();
            var now       = DateTime.Now;

            TotalEmployees      = await db.Employees.CountAsync();
            ActiveEmployees     = await db.Employees.CountAsync(e => e.IsActive);
            GrossPayroll        = await db.Employees.Where(e => e.IsActive).SumAsync(e => e.BaseSalary);
            OutstandingAdvances = await db.Advances.AsNoTracking().SumOutstandingAsync();

            AttendanceSavedThisMonth = await db.AttendanceRecords.CountAsync(
                a => a.Year == now.Year && a.Month == now.Month);

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
    private void BackupDatabase()
    {
        try
        {
            var defaultName = $"salary-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var path = _dialogs.AskSavePath("SQLite Database (*.db)|*.db", defaultName);
            if (path is null) return;
            _backup.Backup(path);
            _dialogs.Info($"Backup saved to:\n{path}");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void RestoreDatabase()
    {
        if (!_dialogs.Confirm(
            "Restoring will replace the current database with the selected backup.\n" +
            "The app will close — restart it after.\n\nContinue?"))
            return;
        try
        {
            var path = _dialogs.AskOpenPath("SQLite Database (*.db)|*.db");
            if (path is null) return;
            _backup.Restore(path);
            _dialogs.Info("Database restored. Please restart the application.");
            Application.Current.Shutdown();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }
}
