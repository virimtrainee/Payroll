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

namespace SalaryManager.App.ViewModels;

public partial class EmployeesViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly DialogService _dialogs;
    private readonly ExcelImportService _importer;

    public Helpers.RangeObservableCollection<Employee> Employees { get; } = new();

    [ObservableProperty] private bool showInactive;
    [ObservableProperty] private Employee? selected;

    public Window? OwnerWindow { get; set; }

    public EmployeesViewModel(IDbContextFactory<AppDbContext> dbf,
                              DatabaseInitializer dbInit,
                              DialogService dialogs,
                              ExcelImportService importer)
    {
        _dbf = dbf;
        _dbInit = dbInit;
        _dialogs = dialogs;
        _importer = importer;
        _ = LoadAsync();
    }

    partial void OnShowInactiveChanged(bool value) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        await _dbInit.ReadyTask;

        using var db = await _dbf.CreateDbContextAsync();
        IQueryable<Employee> query = db.Employees.AsNoTracking().OrderBy(e => e.Name);
        if (!ShowInactive) query = query.Where(e => e.IsActive);
        var list = await query.ToListAsync();

        Employees.ReplaceAll(list);
    }

    [RelayCommand]
    private async Task ImportFromExcelAsync()
    {
        var path = _dialogs.AskOpenPath("Excel Workbook (*.xlsx)|*.xlsx");
        if (path is null) return;

        var (importedRows, error) = _importer.ReadEmployees(path);
        if (error is not null) { _dialogs.Error(error); return; }
        if (importedRows.Count == 0) { _dialogs.Info("No employee rows found in the file."); return; }

        try
        {
            using var db = await _dbf.CreateDbContextAsync();

            // Build case-insensitive set of existing names
            var existing = (await db.Employees.AsNoTracking()
                                .Select(e => e.Name)
                                .ToListAsync())
                           .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0;
            foreach (var row in importedRows)
            {
                if (existing.Contains(row.Name))
                {
                    skipped++;
                    continue;
                }

                db.Employees.Add(new Employee
                {
                    Name          = row.Name,
                    BaseSalary    = row.BaseSalary,
                    AccountNumber = row.AccountNumber,
                    IfscCode      = row.IfscCode,
                    PaymentMode   = row.PaymentMode,
                    IsActive      = true,
                    CreatedAt     = System.DateTime.UtcNow
                });

                existing.Add(row.Name); // prevent duplicates within the same file
                added++;
            }

            if (added > 0) await db.SaveChangesAsync();
            await LoadAsync();

            var msg = $"Imported {added} employee{(added == 1 ? "" : "s")}.";
            if (skipped > 0) msg += $" {skipped} skipped (name already exists).";
            _dialogs.Info(msg);
        }
        catch (System.Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task OpenAddEmployeeAsync()
    {
        var vm = new AddEmployeeViewModel();
        var dialog = new AddEmployeeWindow(vm)
        {
            Owner = OwnerWindow ?? Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            _dialogs.Error("Employee name is required.");
            return;
        }

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            db.Employees.Add(new Employee
            {
                Name          = vm.Name.Trim(),
                BaseSalary    = vm.BaseSalary,
                AccountNumber = string.IsNullOrWhiteSpace(vm.AccountNumber) ? null : vm.AccountNumber.Trim(),
                IfscCode      = string.IsNullOrWhiteSpace(vm.IfscCode) ? null : vm.IfscCode.Trim().ToUpperInvariant(),
                PaymentMode   = vm.PaymentMode,
                JoiningDate   = vm.JoiningDate,
                IsActive      = true,
                CreatedAt     = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            await LoadAsync();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task SaveChangesAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();

        // Load tracked entities so EF's change tracker only emits UPDATEs for
        // rows whose values actually changed.
        var ids = Employees.Select(e => e.Id).ToList();
        var tracked = await db.Employees
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        foreach (var e in Employees)
        {
            if (!tracked.TryGetValue(e.Id, out var t)) continue;

            var oldSalary = t.BaseSalary;

            t.Name          = e.Name;
            t.BaseSalary    = e.BaseSalary;
            t.IsActive      = e.IsActive;
            t.AccountNumber = e.AccountNumber;
            t.IfscCode      = e.IfscCode;
            t.PaymentMode   = e.PaymentMode;
            t.JoiningDate   = e.JoiningDate;

            if (oldSalary != e.BaseSalary)
            {
                db.SalaryRevisions.Add(new SalaryManager.Data.Entities.SalaryRevision
                {
                    EmployeeId = e.Id,
                    OldSalary  = oldSalary,
                    NewSalary  = e.BaseSalary,
                    ChangedAt  = DateTime.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync();
        _dialogs.Info("Saved.");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(Employee? emp)
    {
        if (emp is null) return;
        using var db = await _dbf.CreateDbContextAsync();
        var tracked = await db.Employees.FindAsync(emp.Id);
        if (tracked is null) return;
        tracked.IsActive = !tracked.IsActive;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CyclePaymentModeAsync(Employee? emp)
    {
        if (emp is null) return;
        using var db = await _dbf.CreateDbContextAsync();
        var tracked = await db.Employees.FindAsync(emp.Id);
        if (tracked is null) return;
        tracked.PaymentMode = tracked.PaymentMode switch
        {
            PaymentMode.Cash      => PaymentMode.IciciBank,
            PaymentMode.IciciBank => PaymentMode.OtherBank,
            _                     => PaymentMode.Cash
        };
        await db.SaveChangesAsync();
        await LoadAsync();
    }
}
