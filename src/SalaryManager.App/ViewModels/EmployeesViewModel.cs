using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using SalaryManager.Data.Validation;

namespace SalaryManager.App.ViewModels;

public enum EmployeeStatusFilter
{
    Active,
    Inactive,
    All
}

public sealed record GroupSummaryVm(string Name, int Count);
public sealed class EmployeeDeletedEventArgs(int employeeId) : EventArgs
{
    public int EmployeeId { get; } = employeeId;
}

public partial class EmployeesViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly DialogService _dialogs;
    private readonly ExcelImportService _importer;

    public Helpers.RangeObservableCollection<Employee> Employees { get; } = new();
    public Helpers.RangeObservableCollection<EmployeeGroup> Groups { get; } = new();
    public Helpers.RangeObservableCollection<GroupFilterOptionVm> GroupFilterOptions { get; } = new();
    public Helpers.RangeObservableCollection<GroupSummaryVm> GroupSummaries { get; } = new();
    public Helpers.RangeObservableCollection<GroupEmployeeAssignmentVm> GroupAssignmentRows { get; } = new();
    public EmployeeStatusFilter[] StatusFilters { get; } =
        [EmployeeStatusFilter.Active, EmployeeStatusFilter.Inactive, EmployeeStatusFilter.All];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmployeeListTitle))]
    private bool showInactive;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmployeeListTitle))]
    private EmployeeStatusFilter selectedStatusFilter = EmployeeStatusFilter.Active;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private GroupFilterOptionVm? selectedGroupFilter;
    [ObservableProperty] private Employee? selected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    private EmployeeGroup? selectedGroup;
    [ObservableProperty] private string newGroupName = string.Empty;
    [ObservableProperty] private string selectedGroupName = string.Empty;
    private int _loadVersion;
    private int _groupAssignmentLoadVersion;
    private bool _updatingGroupFilter;

    public event EventHandler<EmployeeDeletedEventArgs>? EmployeePermanentlyDeleted;

    public Window? OwnerWindow { get; set; }
    public bool HasSelectedGroup => SelectedGroup is not null;
    public string EmployeeListTitle => SelectedStatusFilter switch
    {
        EmployeeStatusFilter.Inactive => "Inactive employees",
        EmployeeStatusFilter.All => "All employees",
        _ => "Active employees"
    };

    public EmployeesViewModel(IDbContextFactory<AppDbContext> dbf,
                              DatabaseInitializer dbInit,
                              DialogService dialogs,
                              ExcelImportService importer)
    {
        _dbf = dbf;
        _dbInit = dbInit;
        _dialogs = dialogs;
        _importer = importer;
        LoadCommand.Execute(null);
    }

    partial void OnShowInactiveChanged(bool value) =>
        SelectedStatusFilter = value ? EmployeeStatusFilter.Inactive : EmployeeStatusFilter.Active;
    partial void OnSelectedStatusFilterChanged(EmployeeStatusFilter value)
    {
        ShowInactive = value == EmployeeStatusFilter.Inactive;
        LoadCommand.Execute(null);
    }
    partial void OnSearchTextChanged(string value) => LoadCommand.Execute(null);
    partial void OnSelectedGroupFilterChanged(GroupFilterOptionVm? value)
    {
        if (!_updatingGroupFilter) LoadCommand.Execute(null);
    }
    partial void OnSelectedGroupChanged(EmployeeGroup? value)
    {
        SelectedGroupName = value?.Name ?? string.Empty;
        LoadGroupAssignmentsCommand.Execute(null);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        try
        {
            await _dbInit.ReadyTask;

            using var db = await _dbf.CreateDbContextAsync();
            IQueryable<Employee> query = db.Employees.AsNoTracking()
                .Include(e => e.GroupMemberships)
                    .ThenInclude(m => m.EmployeeGroup)
                .OrderBy(e => e.Name);
            query = SelectedStatusFilter switch
            {
                EmployeeStatusFilter.Inactive => query.Where(e => !e.IsActive),
                EmployeeStatusFilter.All => query,
                _ => query.Where(e => e.IsActive)
            };
            if (SelectedGroupFilter?.Id is int groupId)
                query = query.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

            var list = await query.ToListAsync();
            var search = SearchText.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                list = list
                    .Where(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            var groups = await db.EmployeeGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
            var groupCounts = await db.EmployeeGroupMemberships.AsNoTracking()
                .GroupBy(m => m.EmployeeGroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.GroupId, g => g.Count);

            if (version != _loadVersion) return;
            var selectedFilterId = SelectedGroupFilter?.Id;
            Employees.ReplaceAll(list);
            Groups.ReplaceAll(groups);
            GroupSummaries.ReplaceAll(groups.Select(g => new GroupSummaryVm(g.Name, groupCounts.GetValueOrDefault(g.Id))));

            _updatingGroupFilter = true;
            try
            {
                var filterOptions = new[] { new GroupFilterOptionVm(null, "All Groups") }
                    .Concat(groups.Select(g => new GroupFilterOptionVm(g.Id, g.Name)))
                    .ToList();
                GroupFilterOptions.ReplaceAll(filterOptions);
                SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(g => g.Id == selectedFilterId) ?? GroupFilterOptions[0];
            }
            finally
            {
                _updatingGroupFilter = false;
            }

            SelectedGroup = SelectedGroup is null
                ? Groups.FirstOrDefault()
                : Groups.FirstOrDefault(g => g.Id == SelectedGroup.Id) ?? Groups.FirstOrDefault();
            await LoadGroupAssignmentsAsync();
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportFromExcelAsync()
    {
        var path = _dialogs.AskOpenPath("Excel Workbook (*.xlsx)|*.xlsx");
        if (path is null) return;

        var import = _importer.ReadEmployees(path);
        if (import.Error is not null) { _dialogs.Error(import.Error); return; }
        if (import.Issues.Count > 0)
        {
            _dialogs.Error("Fix the following import errors before importing:\n\n" + FormatImportIssues(import.Issues));
            return;
        }
        if (import.Rows.Count == 0) { _dialogs.Info("No employee rows found in the file."); return; }

        try
        {
            using var db = await _dbf.CreateDbContextAsync();

            // Build case-insensitive set of existing names
            var existing = (await db.Employees.AsNoTracking()
                                .Select(e => e.Name)
                                .ToListAsync())
                           .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            int added = 0, skippedExisting = 0;
            foreach (var row in import.Rows)
            {
                if (existing.Contains(row.Name))
                {
                    skippedExisting++;
                    continue;
                }

                db.Employees.Add(new Employee
                {
                    Name = row.Name,
                    BaseSalary = row.BaseSalary,
                    AccountNumber = row.AccountNumber,
                    IfscCode = row.IfscCode,
                    PaymentMode = row.PaymentMode,
                    IsActive = true,
                    CreatedAt = System.DateTime.UtcNow
                });

                existing.Add(row.Name); // prevent duplicates within the same file
                added++;
            }

            if (added > 0) await db.SaveChangesAsync();
            await LoadAsync();

            var msg = $"Imported {added} employee{(added == 1 ? "" : "s")}.";
            if (skippedExisting > 0) msg += $" {skippedExisting} skipped (name already exists).";
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

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var validation = await ValidateEmployeeAsync(db, vm, null);
            if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

            var employee = new Employee
            {
                Name = vm.Name.Trim(),
                BaseSalary = vm.BaseSalary,
                AccountNumber = vm.PaymentMode == PaymentMode.Cash || string.IsNullOrWhiteSpace(vm.AccountNumber) ? null : vm.AccountNumber.Trim(),
                IfscCode = vm.PaymentMode == PaymentMode.Cash || string.IsNullOrWhiteSpace(vm.IfscCode) ? null : vm.IfscCode.Trim().ToUpperInvariant(),
                PaymentMode = vm.PaymentMode,
                JoiningDate = vm.JoiningDate,
                IsActive = vm.IsActive,
                CreatedAt = DateTime.UtcNow
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

        var validation = await ValidateEmployeeAsync(db, vm, tracked.Id);
        if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

        var oldSalary = tracked.BaseSalary;
        tracked.Name = vm.Name.Trim();
        tracked.BaseSalary = vm.BaseSalary;
        tracked.IsActive = vm.IsActive;
        tracked.AccountNumber = vm.PaymentMode == PaymentMode.Cash || string.IsNullOrWhiteSpace(vm.AccountNumber) ? null : vm.AccountNumber.Trim();
        tracked.IfscCode = vm.PaymentMode == PaymentMode.Cash || string.IsNullOrWhiteSpace(vm.IfscCode) ? null : vm.IfscCode.Trim().ToUpperInvariant();
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
        var allEmployees = await db.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.Name })
            .ToListAsync();
        var issues = new List<ValidationIssue>();

        foreach (var e in Employees)
        {
            if (!tracked.TryGetValue(e.Id, out var t)) continue;

            var duplicateNames = allEmployees
                .Where(existing => existing.Id != e.Id)
                .Select(existing => existing.Name)
                .Concat(Employees.Where(existing => existing.Id != e.Id).Select(existing => existing.Name));
            var validation = EmployeeValidator.Validate(ToEmployeeValidationInput(e), duplicateNames);
            issues.AddRange(validation.Issues.Select(i => i with { Field = $"{e.Name} - {i.Field}" }));
        }

        if (issues.Count > 0)
        {
            _dialogs.Error(new ValidationResult(issues).ToMessage());
            return;
        }

        foreach (var e in Employees)
        {
            if (!tracked.TryGetValue(e.Id, out var t)) continue;

            var oldSalary = t.BaseSalary;

            t.Name = e.Name.Trim();
            t.BaseSalary = e.BaseSalary;
            t.IsActive = e.IsActive;
            t.AccountNumber = e.PaymentMode == PaymentMode.Cash || string.IsNullOrWhiteSpace(e.AccountNumber) ? null : e.AccountNumber.Trim();
            t.IfscCode = e.PaymentMode == PaymentMode.Cash || string.IsNullOrWhiteSpace(e.IfscCode) ? null : e.IfscCode.Trim().ToUpperInvariant();
            t.PaymentMode = e.PaymentMode;
            t.JoiningDate = e.JoiningDate;

            if (oldSalary != e.BaseSalary)
            {
                db.SalaryRevisions.Add(new SalaryManager.Data.Entities.SalaryRevision
                {
                    EmployeeId = e.Id,
                    OldSalary = oldSalary,
                    NewSalary = e.BaseSalary,
                    ChangedAt = DateTime.UtcNow,
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
    private async Task DeleteEmployeeAsync(Employee? emp)
    {
        if (emp is null) return;

        try
        {
            using var countDb = await _dbf.CreateDbContextAsync();
            var impact = new EmployeeDeleteImpact(
                await countDb.AttendanceRecords.AsNoTracking().CountAsync(a => a.EmployeeId == emp.Id),
                await countDb.Advances.AsNoTracking().CountAsync(a => a.EmployeeId == emp.Id),
                await countDb.SalaryRevisions.AsNoTracking().CountAsync(r => r.EmployeeId == emp.Id),
                await countDb.EmployeeGroupMemberships.AsNoTracking().CountAsync(m => m.EmployeeId == emp.Id));

            var message =
                $"Permanently delete '{emp.Name}'?\n\n" +
                "This cannot be undone. The employee and these related records will be deleted:\n\n" +
                $"Attendance records: {impact.AttendanceRecords}\n" +
                $"Advance entries: {impact.Advances}\n" +
                $"Salary revisions: {impact.SalaryRevisions}\n" +
                $"Group memberships: {impact.GroupMemberships}";

            if (!_dialogs.ConfirmDestructive(message, "Permanently delete employee")) return;

            using var db = await _dbf.CreateDbContextAsync();
            var tracked = await db.Employees.FindAsync(emp.Id);
            if (tracked is null) return;

            db.Employees.Remove(tracked);
            await db.SaveChangesAsync();
            await LoadAsync();
            EmployeePermanentlyDeleted?.Invoke(this, new EmployeeDeletedEventArgs(emp.Id));
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
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
            PaymentMode.Cash => PaymentMode.IciciBank,
            PaymentMode.IciciBank => PaymentMode.OtherBank,
            _ => PaymentMode.Cash
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadGroupAssignmentsAsync()
    {
        var version = Interlocked.Increment(ref _groupAssignmentLoadVersion);
        var selectedGroup = SelectedGroup;
        if (selectedGroup is null)
        {
            GroupAssignmentRows.ReplaceAll(Array.Empty<GroupEmployeeAssignmentVm>());
            return;
        }

        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var memberIds = await db.EmployeeGroupMemberships.AsNoTracking()
                .Where(m => m.EmployeeGroupId == selectedGroup.Id)
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

            if (version == _groupAssignmentLoadVersion)
                GroupAssignmentRows.ReplaceAll(rows);
        }
        catch (Exception ex)
        {
            if (version == _groupAssignmentLoadVersion)
            {
                _dialogs.Error(ex.Message);
            }
        }
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

    private static async Task<ValidationResult> ValidateEmployeeAsync(AppDbContext db, AddEmployeeViewModel vm, int? employeeId)
    {
        var existingNames = await db.Employees.AsNoTracking()
            .Where(e => employeeId == null || e.Id != employeeId.Value)
            .Select(e => e.Name)
            .ToListAsync();

        return EmployeeValidator.Validate(ToEmployeeValidationInput(vm), existingNames);
    }

    private static EmployeeValidationInput ToEmployeeValidationInput(AddEmployeeViewModel vm)
        => new(
            vm.Name,
            vm.BaseSalary,
            vm.PaymentMode,
            vm.AccountNumber,
            vm.IfscCode,
            vm.JoiningDate);

    private static EmployeeValidationInput ToEmployeeValidationInput(Employee employee)
        => new(
            employee.Name,
            employee.BaseSalary,
            employee.PaymentMode,
            employee.AccountNumber,
            employee.IfscCode,
            employee.JoiningDate);

    private static string FormatImportIssues(IReadOnlyList<ValidationIssue> issues)
        => ValidationResult.FromErrors(issues).ToDisplayString();

    private sealed record EmployeeDeleteImpact(
        int AttendanceRecords,
        int Advances,
        int SalaryRevisions,
        int GroupMemberships);
}
