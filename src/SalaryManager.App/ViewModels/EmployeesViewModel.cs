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

public partial class EmployeesViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly DialogService _dialogs;
    private readonly ExcelImportService _importer;

    public Helpers.RangeObservableCollection<Employee> Employees { get; } = new();
    public Helpers.RangeObservableCollection<EmployeeGroup> Groups { get; } = new();
    public Helpers.RangeObservableCollection<GroupEmployeeAssignmentVm> GroupAssignmentRows { get; } = new();

    [ObservableProperty] private bool showInactive;
    [ObservableProperty] private Employee? selected;
    [ObservableProperty] private EmployeeGroup? selectedGroup;
    [ObservableProperty] private string newGroupName = string.Empty;
    [ObservableProperty] private string selectedGroupName = string.Empty;

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
    partial void OnSelectedGroupChanged(EmployeeGroup? value)
    {
        SelectedGroupName = value?.Name ?? string.Empty;
        _ = LoadGroupAssignmentsAsync();
    }

    private async Task LoadAsync()
    {
        await _dbInit.ReadyTask;

        using var db = await _dbf.CreateDbContextAsync();
        IQueryable<Employee> query = db.Employees.AsNoTracking().OrderBy(e => e.Name);
        if (!ShowInactive) query = query.Where(e => e.IsActive);
        var list = await query.ToListAsync();
        var groups = await db.EmployeeGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();

        Employees.ReplaceAll(list);
        Groups.ReplaceAll(groups);
        SelectedGroup = SelectedGroup is null
            ? Groups.FirstOrDefault()
            : Groups.FirstOrDefault(g => g.Id == SelectedGroup.Id) ?? Groups.FirstOrDefault();
        await LoadGroupAssignmentsAsync();
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
        using (var loadDb = await _dbf.CreateDbContextAsync())
            await PopulateGroupOptionsAsync(loadDb, vm);

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
            var employee = new Employee
            {
                Name          = vm.Name.Trim(),
                BaseSalary    = vm.BaseSalary,
                AccountNumber = string.IsNullOrWhiteSpace(vm.AccountNumber) ? null : vm.AccountNumber.Trim(),
                IfscCode      = string.IsNullOrWhiteSpace(vm.IfscCode) ? null : vm.IfscCode.Trim().ToUpperInvariant(),
                PaymentMode   = vm.PaymentMode,
                JoiningDate   = vm.JoiningDate,
                IsActive      = vm.IsActive,
                CreatedAt     = DateTime.UtcNow
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            await SaveMembershipsAsync(db, employee.Id, vm.Groups.Where(g => g.IsSelected).Select(g => g.Id));
            await db.SaveChangesAsync();
            await LoadAsync();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task OpenEditEmployeeAsync(Employee? emp)
    {
        if (emp is null) return;

        using var db = await _dbf.CreateDbContextAsync();
        var tracked = await db.Employees
            .Include(e => e.GroupMemberships)
            .FirstOrDefaultAsync(e => e.Id == emp.Id);
        if (tracked is null) return;

        var vm = new AddEmployeeViewModel
        {
            EmployeeId = tracked.Id,
            WindowTitle = "Edit Employee",
            SaveButtonText = "Save Employee",
            Name = tracked.Name,
            BaseSalary = tracked.BaseSalary,
            AccountNumber = tracked.AccountNumber ?? string.Empty,
            IfscCode = tracked.IfscCode ?? string.Empty,
            JoiningDate = tracked.JoiningDate,
            IsActive = tracked.IsActive,
            PaymentMode = tracked.PaymentMode
        };
        await PopulateGroupOptionsAsync(db, vm, tracked.GroupMemberships.Select(m => m.EmployeeGroupId).ToHashSet());

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

        var oldSalary = tracked.BaseSalary;
        tracked.Name = vm.Name.Trim();
        tracked.BaseSalary = vm.BaseSalary;
        tracked.IsActive = vm.IsActive;
        tracked.AccountNumber = string.IsNullOrWhiteSpace(vm.AccountNumber) ? null : vm.AccountNumber.Trim();
        tracked.IfscCode = string.IsNullOrWhiteSpace(vm.IfscCode) ? null : vm.IfscCode.Trim().ToUpperInvariant();
        tracked.PaymentMode = vm.PaymentMode;
        tracked.JoiningDate = vm.JoiningDate;

        if (oldSalary != vm.BaseSalary)
        {
            db.SalaryRevisions.Add(new SalaryRevision
            {
                EmployeeId = tracked.Id,
                OldSalary = oldSalary,
                NewSalary = vm.BaseSalary,
                ChangedAt = DateTime.UtcNow
            });
        }

        await SaveMembershipsAsync(db, tracked.Id, vm.Groups.Where(g => g.IsSelected).Select(g => g.Id));
        await db.SaveChangesAsync();
        await LoadAsync();
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

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        var name = NewGroupName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { _dialogs.Error("Group name is required."); return; }

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            db.EmployeeGroups.Add(new EmployeeGroup { Name = name });
            await db.SaveChangesAsync();
            NewGroupName = string.Empty;
            await LoadAsync();
            SelectedGroup = Groups.FirstOrDefault(g => GroupNameNormalizer.Normalize(g.Name) == GroupNameNormalizer.Normalize(name));
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task RenameGroupAsync()
    {
        if (SelectedGroup is null) return;
        var name = SelectedGroupName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { _dialogs.Error("Group name is required."); return; }

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var group = await db.EmployeeGroups.FindAsync(SelectedGroup.Id);
            if (group is null) return;
            group.Name = name;
            await db.SaveChangesAsync();
            await LoadAsync();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task DeleteGroupAsync()
    {
        if (SelectedGroup is null) return;
        if (!_dialogs.Confirm($"Delete group '{SelectedGroup.Name}'? Employees will not be deleted.")) return;

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var group = await db.EmployeeGroups.FindAsync(SelectedGroup.Id);
            if (group is null) return;
            db.EmployeeGroups.Remove(group);
            await db.SaveChangesAsync();
            await LoadAsync();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task SaveGroupAssignmentsAsync()
    {
        if (SelectedGroup is null) return;

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var current = await db.EmployeeGroupMemberships
                .Where(m => m.EmployeeGroupId == SelectedGroup.Id)
                .ToListAsync();
            var visibleEmployeeIds = GroupAssignmentRows.Select(r => r.EmployeeId).ToHashSet();
            var selectedIds = GroupAssignmentRows
                .Where(r => r.IsMember)
                .Select(r => r.EmployeeId)
                .ToHashSet();

            db.EmployeeGroupMemberships.RemoveRange(current.Where(m =>
                visibleEmployeeIds.Contains(m.EmployeeId) && !selectedIds.Contains(m.EmployeeId)));
            var existingIds = current.Select(m => m.EmployeeId).ToHashSet();
            foreach (var employeeId in selectedIds.Where(id => !existingIds.Contains(id)))
            {
                db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership
                {
                    EmployeeId = employeeId,
                    EmployeeGroupId = SelectedGroup.Id
                });
            }

            await db.SaveChangesAsync();
            _dialogs.Info("Group assignments saved.");
            await LoadAsync();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    private async Task LoadGroupAssignmentsAsync()
    {
        if (SelectedGroup is null)
        {
            GroupAssignmentRows.ReplaceAll(Array.Empty<GroupEmployeeAssignmentVm>());
            return;
        }

        using var db = await _dbf.CreateDbContextAsync();
        var memberIds = await db.EmployeeGroupMemberships.AsNoTracking()
            .Where(m => m.EmployeeGroupId == SelectedGroup.Id)
            .Select(m => m.EmployeeId)
            .ToHashSetAsync();

        var rows = Employees
            .OrderBy(e => e.Name)
            .Select(e => new GroupEmployeeAssignmentVm
            {
                EmployeeId = e.Id,
                EmployeeName = e.Name,
                IsMember = memberIds.Contains(e.Id)
            })
            .ToList();
        GroupAssignmentRows.ReplaceAll(rows);
    }

    private static async Task PopulateGroupOptionsAsync(AppDbContext db, AddEmployeeViewModel vm, HashSet<int>? selectedIds = null)
    {
        selectedIds ??= new HashSet<int>();
        var groups = await db.EmployeeGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
        vm.Groups.Clear();
        foreach (var group in groups)
        {
            vm.Groups.Add(new GroupMembershipOptionVm
            {
                Id = group.Id,
                Name = group.Name,
                IsSelected = selectedIds.Contains(group.Id)
            });
        }
    }

    private static async Task SaveMembershipsAsync(AppDbContext db, int employeeId, IEnumerable<int> selectedGroupIds)
    {
        var selected = selectedGroupIds.ToHashSet();
        var current = await db.EmployeeGroupMemberships
            .Where(m => m.EmployeeId == employeeId)
            .ToListAsync();

        db.EmployeeGroupMemberships.RemoveRange(current.Where(m => !selected.Contains(m.EmployeeGroupId)));
        var existing = current.Select(m => m.EmployeeGroupId).ToHashSet();
        foreach (var groupId in selected.Where(id => !existing.Contains(id)))
        {
            db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership
            {
                EmployeeId = employeeId,
                EmployeeGroupId = groupId
            });
        }
    }
}
