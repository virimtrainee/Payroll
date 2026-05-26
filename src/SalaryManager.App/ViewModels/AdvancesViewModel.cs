using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.App.ViewModels;

public class EmployeeWithBalanceVm
{
    public int     Id          { get; init; }
    public string  Name        { get; init; } = "";
    public string  Initials    { get; init; } = "";
    public decimal BaseSalary  { get; init; }
    public decimal Balance     { get; init; }
}

public partial class AdvanceEntryEditVm : ObservableObject
{
    public AdvanceEntryType[] EntryTypes { get; } = [AdvanceEntryType.Given, AdvanceEntryType.Deducted];

    [ObservableProperty] private DateTime? date = DateTime.Today;
    [ObservableProperty] private AdvanceEntryType entryType = AdvanceEntryType.Given;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string? note;
}

public partial class AdvancesViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly DialogService _dialogs;

    public ObservableCollection<Employee>              Employees    { get; } = new();
    public RangeObservableCollection<EmployeeWithBalanceVm> EmployeeRail { get; } = new();
    public ICollectionView                             EmployeeRailView { get; }
    public RangeObservableCollection<LedgerRow>        Ledger       { get; } = new();
    public RangeObservableCollection<GroupFilterOptionVm> GroupFilterOptions { get; } = new();

    [ObservableProperty] private EmployeeWithBalanceVm? selectedRailItem;
    [ObservableProperty] private Employee?               selectedEmployee;
    [ObservableProperty] private decimal                 currentBalance;
    [ObservableProperty] private string                  selectedEmployeeInitials = "";
    [ObservableProperty] private decimal                 newAmount;
    [ObservableProperty] private string?                 newNote;
    [ObservableProperty] private string                  searchText = string.Empty;
    [ObservableProperty] private bool                    showOnlyWithAdvances;
    [ObservableProperty] private int                     filteredCount;
    [ObservableProperty] private bool                    isLoading;
    [ObservableProperty] private GroupFilterOptionVm?     selectedGroupFilter;
    private bool _updatingGroupFilter;

    partial void OnSearchTextChanged(string value)          => RefreshFilter();
    partial void OnShowOnlyWithAdvancesChanged(bool value)  => RefreshFilter();
    partial void OnSelectedGroupFilterChanged(GroupFilterOptionVm? value)
    {
        if (!_updatingGroupFilter) _ = LoadAsync();
    }

    private void RefreshFilter()
    {
        EmployeeRailView.Refresh();
        FilteredCount = EmployeeRailView.Cast<EmployeeWithBalanceVm>().Count();
    }

    private bool RailFilter(object item)
    {
        if (item is not EmployeeWithBalanceVm e) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            return false;
        if (ShowOnlyWithAdvances && e.Balance == 0) return false;
        return true;
    }

    public AdvancesViewModel(IDbContextFactory<AppDbContext> dbf,
                             DatabaseInitializer dbInit,
                             DialogService dialogs)
    {
        _dbf = dbf;
        _dbInit = dbInit;
        _dialogs = dialogs;

        EmployeeRailView = CollectionViewSource.GetDefaultView(EmployeeRail);
        EmployeeRailView.Filter = RailFilter;
    }

    partial void OnSelectedRailItemChanged(EmployeeWithBalanceVm? value)
    {
        if (value is null) return;
        SelectedEmployee = Employees.FirstOrDefault(e => e.Id == value.Id);
    }

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        SelectedEmployeeInitials = GetInitials(value?.Name ?? "");
        _ = LoadLedgerAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
        await _dbInit.ReadyTask;

        var prevId = SelectedEmployee?.Id;

        using var db = await _dbf.CreateDbContextAsync();
        await LoadGroupFiltersAsync(db);

        var employeeQuery = db.Employees.AsNoTracking().Where(e => e.IsActive);
        if (SelectedGroupFilter?.Id is int groupId)
            employeeQuery = employeeQuery.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

        var list = await employeeQuery.OrderBy(e => e.Name).ToListAsync();

        var ids = list.Select(e => e.Id).ToList();
        var balances = await db.Advances.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId))
            .SumBalancesByEmployeeAsync();

        Employees.Clear();
        var rail = new List<EmployeeWithBalanceVm>(list.Count);
        foreach (var e in list)
        {
            Employees.Add(e);
            rail.Add(new EmployeeWithBalanceVm
            {
                Id         = e.Id,
                Name       = e.Name,
                Initials   = GetInitials(e.Name),
                BaseSalary = e.BaseSalary,
                Balance    = balances.GetValueOrDefault(e.Id, 0m),
            });
        }
        EmployeeRail.ReplaceAll(rail);

        RefreshFilter();

        // Restore previous selection (or pick the first visible item)
        var toSelect = prevId.HasValue
            ? rail.FirstOrDefault(r => r.Id == prevId.Value && RailFilter(r))
            : null;
        toSelect ??= EmployeeRailView.Cast<EmployeeWithBalanceVm>().FirstOrDefault();
        SelectedRailItem = toSelect;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadLedgerAsync()
    {
        if (SelectedEmployee is null)
        {
            Ledger.ReplaceAll(System.Linq.Enumerable.Empty<LedgerRow>());
            CurrentBalance = 0;
            return;
        }

        using var db = await _dbf.CreateDbContextAsync();
        var entries = await db.Advances.AsNoTracking()
            .Where(a => a.EmployeeId == SelectedEmployee.Id)
            .OrderBy(a => a.Date).ThenBy(a => a.Id)
            .ToListAsync();

        var rows = AdvanceLedger.Build(entries);
        Ledger.ReplaceAll(rows);
        CurrentBalance = rows.LastOrDefault()?.RunningBalance ?? 0m;
    }

    [RelayCommand]
    private async Task AddEntryAsync()
    {
        if (SelectedEmployee is null) { _dialogs.Error("Pick an employee first."); return; }
        if (NewAmount <= 0)           { _dialogs.Error("Amount must be greater than zero."); return; }

        using var db = await _dbf.CreateDbContextAsync();
        db.Advances.Add(new Advance
        {
            EmployeeId = SelectedEmployee.Id,
            Date       = DateTime.Today,
            Amount     = NewAmount,
            EntryType  = AdvanceEntryType.Given,
            Note       = string.IsNullOrWhiteSpace(NewNote) ? null : NewNote.Trim()
        });
        await db.SaveChangesAsync();

        NewAmount = 0;
        NewNote   = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EditEntryAsync(LedgerRow? row)
    {
        if (row?.Entry is null) return;

        var vm = new AdvanceEntryEditVm
        {
            Date = row.Entry.Date,
            EntryType = row.Entry.EntryType,
            Amount = row.Entry.Amount,
            Note = row.Entry.Note
        };

        var dialog = new AdvanceEntryWindow(vm)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return;
        if (vm.Amount <= 0) { _dialogs.Error("Amount must be greater than zero."); return; }

        using var db = await _dbf.CreateDbContextAsync();
        var advance = await db.Advances.FindAsync(row.Entry.Id);
        if (advance is null) return;
        if (AdvanceSourceKeys.TryParseSalary(advance.SourceKey, out var salaryYear, out var salaryMonth))
        {
            advance.Date = new DateTime(salaryYear, salaryMonth, 1);
            advance.EntryType = AdvanceEntryType.Deducted;
        }
        else
        {
            advance.Date = (vm.Date ?? DateTime.Today).Date;
            advance.EntryType = vm.EntryType;
        }
        advance.Amount = vm.Amount;
        advance.Note = string.IsNullOrWhiteSpace(vm.Note) ? null : vm.Note.Trim();
        await db.SaveChangesAsync();
        await LoadAsync();
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

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(p => char.ToUpper(p[0])));
    }
}
