using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using Xunit;

namespace SalaryManager.Tests;

public class EmployeeSalarySheetFilterTests
{
    [Fact]
    public async Task EmployeesLoad_ShowInactiveSwitchesBetweenActiveAndInactiveRows()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Active A", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 2, Name = "Inactive B", BaseSalary = 1000m, IsActive = false },
                new Employee { Id = 3, Name = "Active C", BaseSalary = 1000m, IsActive = true });
        });

        var viewModel = NewEmployeesViewModel(factory, initializer);

        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Active employees", viewModel.EmployeeListTitle);
        Assert.Equal(["Active A", "Active C"], viewModel.Employees.Select(e => e.Name));

        viewModel.ShowInactive = true;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Inactive employees", viewModel.EmployeeListTitle);
        Assert.Equal(["Inactive B"], viewModel.Employees.Select(e => e.Name));
    }

    [Fact]
    public async Task EmployeesLoad_SearchFiltersByNameWithinCurrentStatus()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Alice", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 2, Name = "Alicia", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 3, Name = "Bob", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 4, Name = "Inactive Alice", BaseSalary = 1000m, IsActive = false });
        });

        var viewModel = NewEmployeesViewModel(factory, initializer);
        viewModel.SearchText = "ALI";

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Alice", "Alicia"], viewModel.Employees.Select(e => e.Name));
    }

    [Fact]
    public async Task EmployeesDelete_PermanentlyDeletesRelatedRecordsAndPreservesGroups()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.EmployeeGroups.Add(new EmployeeGroup { Id = 10, Name = "Factory" });
            db.Employees.Add(new Employee { Id = 1, Name = "Delete Me", BaseSalary = 1000m, IsActive = true });
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = 1,
                Year = 2026,
                Month = 5,
                DaysAbsent = 1
            });
            db.Advances.Add(new Advance
            {
                EmployeeId = 1,
                Date = new DateTime(2026, 5, 1),
                EntryType = AdvanceEntryType.Given,
                Amount = 500m
            });
            db.SalaryRevisions.Add(new SalaryRevision
            {
                EmployeeId = 1,
                OldSalary = 900m,
                NewSalary = 1000m,
                ChangedAt = new DateTime(2026, 5, 1)
            });
            db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership
            {
                EmployeeId = 1,
                EmployeeGroupId = 10
            });
        });

        var dialogs = NewDialogService();
        var viewModel = new EmployeesViewModel(factory, initializer, dialogs, new ExcelImportService());
        await viewModel.LoadCommand.ExecuteAsync(null);

        var employee = Assert.Single(viewModel.Employees);
        await viewModel.DeleteEmployeeCommand.ExecuteAsync(employee);

        _ = dialogs.Received(1).ConfirmDestructiveAsync(
            Arg.Is<string>(prompt =>
                prompt.Contains("Attendance records: 1") &&
                prompt.Contains("Advance entries: 1") &&
                prompt.Contains("Salary revisions: 1") &&
                prompt.Contains("Group memberships: 1")),
            "Permanently delete employee");

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(await verifyDb.Employees.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyDb.AttendanceRecords.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyDb.Advances.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyDb.SalaryRevisions.AsNoTracking().ToListAsync());
        Assert.Empty(await verifyDb.EmployeeGroupMemberships.AsNoTracking().ToListAsync());
        Assert.Equal("Factory", Assert.Single(await verifyDb.EmployeeGroups.AsNoTracking().ToListAsync()).Name);
    }

    [Fact]
    public async Task Advances_DefaultsToBalanceOnlyFilter()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);

        var viewModel = new AdvancesViewModel(factory, initializer, NewDialogService());

        Assert.True(viewModel.ShowOnlyWithAdvances);
    }

    [Fact]
    public async Task SalarySheetLoad_LoadsAllGroupsForTableFiltering()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.EmployeeGroups.AddRange(
                new EmployeeGroup { Id = 1, Name = "Factory" },
                new EmployeeGroup { Id = 2, Name = "Night" });
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Both", BaseSalary = 20000m, IsActive = true },
                new Employee { Id = 2, Name = "Factory Only", BaseSalary = 20000m, IsActive = true },
                new Employee { Id = 3, Name = "Night Only", BaseSalary = 20000m, IsActive = true },
                new Employee { Id = 4, Name = "Ungrouped", BaseSalary = 20000m, IsActive = true });
            db.EmployeeGroupMemberships.AddRange(
                new EmployeeGroupMembership { EmployeeId = 1, EmployeeGroupId = 1 },
                new EmployeeGroupMembership { EmployeeId = 1, EmployeeGroupId = 2 },
                new EmployeeGroupMembership { EmployeeId = 2, EmployeeGroupId = 1 },
                new EmployeeGroupMembership { EmployeeId = 3, EmployeeGroupId = 2 });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Both", "Factory Only", "Night Only", "Ungrouped"], viewModel.Rows.Select(r => r.Name));
        Assert.Equal(["Factory", "Factory", "Night", "Other"], viewModel.Rows.Select(r => r.GroupDisplayName));
        Assert.Empty(viewModel.RowsView.GroupDescriptions);
    }

    [Fact]
    public async Task SalarySheetSave_SalaryPaidPersistsReloadsAndUpdatesTotals()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(10000m, row.SalaryPaid);
        Assert.Equal(10000m, row.EffectiveBaseSalary);

        row.SalaryPaid = 12000m;

        Assert.True(row.IsBaseSalaryOverrideEnabled);
        Assert.Equal(12000m, row.BaseSalaryOverride);
        Assert.Equal(12000m, row.SalaryPaid);
        Assert.Equal(10000m, row.BaseSalary);
        Assert.Equal(10000m, row.EmployeeBaseSalary);
        Assert.Equal(10000m, row.EffectiveBaseSalary);
        Assert.Equal(12000m, row.EffectiveSalaryPaid);
        Assert.Equal(12000m, row.NetSalary);
        viewModel.FlushPendingTotalRefresh();
        Assert.Equal(12000m, viewModel.TotalGross);
        Assert.Equal(12000m, viewModel.TotalNet);
        Assert.Equal(0m, viewModel.TotalDeduction);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using (var verifyDb = factory.CreateDbContext())
        {
            var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
            Assert.Equal(12000m, saved.BaseSalaryOverride);
            Assert.Null(saved.NetSalaryOverride);
        }

        await viewModel.LoadCommand.ExecuteAsync(null);

        var reloaded = Assert.Single(viewModel.Rows);
        Assert.True(reloaded.IsBaseSalaryOverrideEnabled);
        Assert.Equal(12000m, reloaded.BaseSalaryOverride);
        Assert.Equal(12000m, reloaded.SalaryPaid);
        Assert.Equal(10000m, reloaded.BaseSalary);
        Assert.Equal(10000m, reloaded.EmployeeBaseSalary);
        Assert.Equal(10000m, reloaded.EffectiveBaseSalary);
        Assert.Equal(12000m, reloaded.EffectiveSalaryPaid);
        Assert.Equal(12000m, reloaded.NetSalary);
        Assert.Equal(12000m, viewModel.TotalNet);
    }

    [Fact]
    public async Task SalarySheetSave_BaseSalaryPaidValueDoesNotPersistOverride()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(10000m, row.SalaryPaid);

        row.SalaryPaid = 10000m;

        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(10000m, row.EffectiveBaseSalary);
        Assert.Equal(10000m, row.EffectiveSalaryPaid);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using (var verifyDb = factory.CreateDbContext())
        {
            var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
            Assert.Null(saved.BaseSalaryOverride);
        }

        row.SalaryPaid = 12000m;
        row.SalaryPaid = 10000m;

        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(10000m, row.SalaryPaid);
        Assert.Equal(10000m, row.EffectiveBaseSalary);
        Assert.Equal(10000m, row.EffectiveSalaryPaid);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using (var verifyDb = factory.CreateDbContext())
        {
            var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
            Assert.Null(saved.BaseSalaryOverride);
        }
    }

    [Fact]
    public async Task SalarySheetSalaryPaid_DefaultFollowsAbsenceUntilManualOverride()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 30000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(30000m, row.SalaryPaid);

        row.DaysAbsent = 1;

        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Equal(29032.26m, row.DefaultSalaryPaid);
        Assert.Equal(29032.26m, row.SalaryPaid);
        Assert.Equal(29032.26m, row.EffectiveSalaryPaid);
        Assert.Equal(29032.26m, row.NetSalary);

        row.SalaryPaid = 28000m;
        row.DaysAbsent = 2;

        Assert.True(row.IsBaseSalaryOverrideEnabled);
        Assert.Equal(28064.52m, row.DefaultSalaryPaid);
        Assert.Equal(28000m, row.SalaryPaid);
        Assert.Equal(28000m, row.EffectiveSalaryPaid);
        Assert.Equal(28000m, row.NetSalary);

        row.SalaryPaid = row.DefaultSalaryPaid;

        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(28064.52m, row.SalaryPaid);
    }

    [Fact]
    public async Task SalarySheetLock_SavesAndLeavesEditModeOnlyOnSuccess()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.IsEditMode = true;
        var row = Assert.Single(viewModel.Rows);
        row.SalaryPaid = 12000m;

        await viewModel.ToggleEditModeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEditMode);
        using var verifyDb = factory.CreateDbContext();
        var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Equal(12000m, saved.BaseSalaryOverride);
    }

    [Fact]
    public async Task SalarySheetLock_StaysEditableWhenSaveValidationFails()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var dialogs = NewDialogService();
        var viewModel = NewSalarySheetViewModel(factory, initializer, dialogs);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.IsEditMode = true;
        var row = Assert.Single(viewModel.Rows);
        row.IsBaseSalaryOverrideEnabled = true;
        row.BaseSalaryOverride = null;

        await viewModel.ToggleEditModeCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditMode);
        await dialogs.Received().ErrorAsync(
            Arg.Is<string>(message => message.Contains("Salary paid is required.")),
            Arg.Any<string>());
    }

    [Fact]
    public async Task SalarySheetReset_WarnsAndOnlyReloadsWhenConfirmed()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var dialogs = NewDialogService();
        dialogs.ConfirmWarningAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var viewModel = NewSalarySheetViewModel(factory, initializer, dialogs);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        row.SalaryPaid = 12000m;

        await viewModel.ResetTableCommand.ExecuteAsync(null);

        Assert.Equal(12000m, row.SalaryPaid);

        dialogs.ConfirmWarningAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        await viewModel.ResetTableCommand.ExecuteAsync(null);

        var reloaded = Assert.Single(viewModel.Rows);
        Assert.Equal(10000m, reloaded.SalaryPaid);
        await dialogs.Received(2).ConfirmWarningAsync(
            Arg.Is<string>(message => message.Contains("Unsaved changes")),
            Arg.Any<string>(),
            Arg.Is<string>(text => text == "Reset"));
    }

    [Fact]
    public async Task SalarySheetTotals_UpdateAfterDeductionsAdvanceAndOverrideChanges()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 30000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        row.DaysAbsent = 1;
        row.TdsDeduction = 100m;
        row.AdvanceDeductionEntry = 50m;
        viewModel.FlushPendingTotalRefresh();

        Assert.Equal(1, viewModel.TotalDaysAbsent);
        Assert.Equal(100m, viewModel.TotalTds);
        Assert.Equal(50m, viewModel.TotalAdvanceDeduction);
        Assert.Equal(967.74m, viewModel.TotalAbsenceDeduction);
        Assert.Equal(28882.26m, viewModel.TotalNet);

        row.SalaryPaid = 20000m;

        Assert.True(row.UsesEsicPf);
        Assert.False(row.UsesTds);
        Assert.Equal(0m, row.TdsDeduction);
        viewModel.FlushPendingTotalRefresh();
        Assert.Equal(20000m, viewModel.TotalGross);
        Assert.Equal(19950m, viewModel.TotalNet);
        Assert.Equal(50m, viewModel.TotalDeduction);
    }

    [Fact]
    public async Task SalarySheetTotals_DebouncesAggregateRefreshAndCanFlush()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 30000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(30000m, viewModel.TotalNet);

        row.DaysAbsent = 1;

        Assert.Equal(30000m, viewModel.TotalNet);

        await Task.Delay(SalarySheetViewModel.TotalRefreshDebounceDelay + TimeSpan.FromMilliseconds(80));

        Assert.Equal(29032.26m, viewModel.TotalNet);

        row.DaysAbsent = 2;
        viewModel.FlushPendingTotalRefresh();

        Assert.Equal(28064.52m, viewModel.TotalNet);
    }

    [Fact]
    public async Task SalarySheetBulkActions_UpdateSalaryPaidAndDeductions()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 30000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.IsEditMode = true;

        var row = Assert.Single(viewModel.Rows);
        row.SalaryPaid = 20000m;
        row.TdsDeduction = 125m;
        row.AdvanceDeductionEntry = 75m;
        viewModel.FlushPendingTotalRefresh();

        viewModel.ResetSalaryPaidToCalculated([row]);
        viewModel.FlushPendingTotalRefresh();

        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(30000m, row.SalaryPaid);
        Assert.Equal(30000m, viewModel.TotalGross);

        viewModel.ZeroDeductions([row], ["tds"]);
        viewModel.FlushPendingTotalRefresh();

        Assert.Equal(0m, row.TdsDeduction);
        Assert.Equal(75m, row.AdvanceDeductionEntry);
        Assert.Equal(75m, viewModel.TotalAdvanceDeduction);

        viewModel.ZeroDeductions([row], []);
        viewModel.FlushPendingTotalRefresh();

        Assert.Equal(0m, row.EsicDeduction);
        Assert.Equal(0m, row.PfDeduction);
        Assert.Equal(0m, row.TdsDeduction);
        Assert.Equal(0m, row.AdvanceDeductionEntry);
        Assert.Equal(0m, viewModel.TotalAdvanceDeduction);
    }

    [Fact]
    public async Task SalarySheetBulkAdvanceDeduction_RejectsNegativeAndUpdatesTotals()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.IsEditMode = true;

        var row = Assert.Single(viewModel.Rows);

        Assert.False(viewModel.ApplyAdvanceDeduction([row], -1m));
        Assert.Equal(0m, row.AdvanceDeductionEntry);

        Assert.True(viewModel.ApplyAdvanceDeduction([row], 250m));
        viewModel.FlushPendingTotalRefresh();

        Assert.Equal(250m, row.AdvanceDeductionEntry);
        Assert.Equal(250m, viewModel.TotalAdvanceDeduction);
        Assert.Equal(9750m, viewModel.TotalNet);
    }

    [Fact]
    public void SalarySheetSelectionSnapshot_OrdersSelectedRowsAndVisibleColumns()
    {
        var rowA = new SalaryRowVm { Name = "A", EmployeeBaseSalary = 10000m };
        var rowB = new SalaryRowVm { Name = "B", EmployeeBaseSalary = 10000m };
        var rowC = new SalaryRowVm { Name = "C", EmployeeBaseSalary = 10000m };
        var columns = new[]
        {
            new SalarySheetColumnSelection("employee", "Name", "Employee"),
            new SalarySheetColumnSelection("absent", "DaysAbsent", "Absent"),
            new SalarySheetColumnSelection("salaryPaid", "SalaryPaid", "Salary Paid"),
            new SalarySheetColumnSelection("tds", "TdsDeduction", "TDS"),
        };
        var cells = new[]
        {
            new SalarySheetCellSelection(rowC, "tds", 3),
            new SalarySheetCellSelection(rowA, "salaryPaid", 1),
            new SalarySheetCellSelection(rowC, "absent", 3),
        };

        var snapshot = SalarySheetGridSelection.FromCellSelections(columns, cells);

        Assert.Equal(["A", "C"], snapshot.Rows.Select(row => row.Name));
        Assert.Equal(["absent", "salaryPaid", "tds"], snapshot.ColumnKeys);
        Assert.Equal(["DaysAbsent", "SalaryPaid", "TdsDeduction"], snapshot.MappingNames);
        Assert.True(snapshot.Contains(rowC, "TdsDeduction"));
        Assert.False(snapshot.Contains(rowB, "TdsDeduction"));
    }

    [Fact]
    public async Task SalarySheetSave_BlankOrNegativeBaseOverrideIsBlocked()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
        });

        var dialogs = NewDialogService();
        var viewModel = NewSalarySheetViewModel(factory, initializer, dialogs);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        row.IsBaseSalaryOverrideEnabled = true;
        row.BaseSalaryOverride = null;

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        await dialogs.Received().ErrorAsync(
            Arg.Is<string>(message => message.Contains("Salary paid is required.")),
            Arg.Any<string>());

        row.SalaryPaid = -1m;
        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        await dialogs.Received().ErrorAsync(
            Arg.Is<string>(message => message.Contains("Salary paid cannot be negative.")),
            Arg.Any<string>());

        using var verifyDb = factory.CreateDbContext();
        Assert.Empty(await verifyDb.AttendanceRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SalarySheetSave_ClearingSalaryPaidClearsOverrideAndLegacyNetOverride()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = 1,
                Year = 2026,
                Month = 5,
                BaseSalaryOverride = 12000m,
                NetSalaryOverride = 7777m
            });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.True(row.IsBaseSalaryOverrideEnabled);
        Assert.Equal(12000m, row.SalaryPaid);
        Assert.Equal(10000m, row.EffectiveBaseSalary);
        Assert.Equal(12000m, row.EffectiveSalaryPaid);
        Assert.Equal(12000m, row.NetSalary);

        row.SalaryPaid = null;

        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(10000m, row.SalaryPaid);
        Assert.Equal(10000m, row.EffectiveBaseSalary);
        Assert.Equal(10000m, row.EffectiveSalaryPaid);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using var verifyDb = factory.CreateDbContext();
        var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Null(saved.BaseSalaryOverride);
        Assert.Null(saved.NetSalaryOverride);
    }

    [Fact]
    public async Task SalarySheetLoad_HistoricalNetOverrideStillAppliesUntilSalarySheetSave()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = 1,
                Year = 2026,
                Month = 5,
                NetSalaryOverride = 7777m
            });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Equal(7777m, row.NetSalary);
        Assert.Equal(7777m, viewModel.TotalNet);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using var verifyDb = factory.CreateDbContext();
        var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Null(saved.BaseSalaryOverride);
        Assert.Null(saved.NetSalaryOverride);
    }

    [Fact]
    public async Task SalarySheetLoad_JoiningMonthWithoutAttendanceAppliesProRata()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee
            {
                Id = 1,
                Name = "A",
                BaseSalary = 10000m,
                JoiningDate = new DateTime(2026, 5, 10),
                IsActive = true
            });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(9, row.ProRataDays);
        Assert.Equal(9, row.DaysAbsent);
    }

    [Fact]
    public async Task SalarySheetLoad_ExistingJoiningMonthAttendanceSuppressesProRata()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee
            {
                Id = 1,
                Name = "A",
                BaseSalary = 10000m,
                JoiningDate = new DateTime(2026, 5, 10),
                IsActive = true
            });
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = 1,
                Year = 2026,
                Month = 5,
                DaysAbsent = 0
            });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(0, row.ProRataDays);
        Assert.Equal(0, row.DaysAbsent);
    }

    [Fact]
    public async Task SalarySheetLoad_BaseOverrideEqualToEmployeeSalaryUsesDefaultSalaryPaid()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = 1,
                Year = 2026,
                Month = 5,
                BaseSalaryOverride = 10000m
            });
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.False(row.IsBaseSalaryOverrideEnabled);
        Assert.Null(row.BaseSalaryOverride);
        Assert.Equal(10000m, row.SalaryPaid);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using var verifyDb = factory.CreateDbContext();
        var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Null(saved.BaseSalaryOverride);
    }

    [Fact]
    public void SalarySheetGridNavigation_MovesAcrossEditableCellsBeforeNextRow()
    {
        var esicPfRow = new SalaryRowVm
        {
            Name = "ESIC PF",
            EmployeeBaseSalary = 20000m,
            Year = 2026,
            Month = 5
        };
        esicPfRow.InitializeEditableValues(false, null, 0, 0m, 0m, 0m, 0m);

        var tdsRow = new SalaryRowVm
        {
            Name = "TDS",
            EmployeeBaseSalary = 30000m,
            Year = 2026,
            Month = 5
        };
        tdsRow.InitializeEditableValues(false, null, 0, 0m, 0m, 0m, 0m);

        Assert.Equal(
            ["absent", "salaryPaid", "esic", "pf", "advanceDeduction"],
            SalarySheetGridNavigation.GetEditableColumnKeys(esicPfRow));
        Assert.Equal(
            ["absent", "salaryPaid", "tds", "advanceDeduction"],
            SalarySheetGridNavigation.GetEditableColumnKeys(tdsRow));

        var rows = new[] { esicPfRow, tdsRow };
        Assert.Equal((0, "salaryPaid"), SalarySheetGridNavigation.FindNextEditableColumnKey(rows, 0, "absent"));
        Assert.Equal((0, "esic"), SalarySheetGridNavigation.FindNextEditableColumnKey(rows, 0, "salaryPaid"));
        Assert.Equal((0, "pf"), SalarySheetGridNavigation.FindNextEditableColumnKey(rows, 0, "esic"));
        Assert.Equal((0, "advanceDeduction"), SalarySheetGridNavigation.FindNextEditableColumnKey(rows, 0, "pf"));
        Assert.Equal((1, "absent"), SalarySheetGridNavigation.FindNextEditableColumnKey(rows, 0, "advanceDeduction"));
        Assert.Equal((1, "tds"), SalarySheetGridNavigation.FindNextEditableColumnKey(rows, 1, "salaryPaid"));
        Assert.Equal(
            (0, "salaryPaid"),
            SalarySheetGridNavigation.FindNextEditableColumnKey(
                rows,
                0,
                "absent",
                new HashSet<string> { "salaryPaid", "absent", "advanceDeduction" }));
    }

    [Fact]
    public async Task AttendanceSave_PreservesBaseOverrideAndUsesEffectiveSalaryForDeductions()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 30000m, IsActive = true });
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = 1,
                Year = 2026,
                Month = 5,
                BaseSalaryOverride = 20000m,
                EsicDeduction = 100m,
                PfDeduction = 200m,
                TdsDeduction = 500m
            });
        });

        var viewModel = NewAttendanceViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal(30000m, row.BaseSalary);
        Assert.Equal(20000m, row.EffectiveSalaryPaid);
        Assert.True(row.UsesEsicPf);
        Assert.False(row.UsesTds);
        Assert.Equal(100m, row.EsicDeduction);
        Assert.Equal(200m, row.PfDeduction);
        Assert.Equal(0m, row.TdsDeduction);

        await viewModel.SaveCommand.ExecuteAsync(null);

        using var verifyDb = factory.CreateDbContext();
        var saved = await verifyDb.AttendanceRecords.AsNoTracking().SingleAsync();
        Assert.Equal(20000m, saved.BaseSalaryOverride);
        Assert.Equal(100m, saved.EsicDeduction);
        Assert.Equal(200m, saved.PfDeduction);
        Assert.Equal(0m, saved.TdsDeduction);
    }

    [Fact]
    public async Task ReportsKpis_UseBaseOverrideAndHistoricalNetOverrideFallback()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Legacy", BaseSalary = 10000m, IsActive = true },
                new Employee { Id = 2, Name = "Base Override", BaseSalary = 30000m, IsActive = true });
            db.AttendanceRecords.AddRange(
                new AttendanceRecord { EmployeeId = 1, Year = 2026, Month = 5, NetSalaryOverride = 7777m },
                new AttendanceRecord { EmployeeId = 2, Year = 2026, Month = 5, BaseSalaryOverride = 20000m });
        });

        var viewModel = NewReportsViewModel(factory, initializer);
        await viewModel.LoadKpisCommand.ExecuteAsync(null);

        Assert.Contains("30,000", viewModel.KpiGross);
        Assert.Contains("27,777", viewModel.KpiTotalPayable);
        Assert.Contains("2,223", viewModel.KpiDeductions);
    }

    [Fact]
    public async Task DashboardPayroll_UsesBaseOverrideAndHistoricalNetOverrideFallback()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var now = DateTime.Now;
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Legacy", BaseSalary = 10000m, IsActive = true },
                new Employee { Id = 2, Name = "Base Override", BaseSalary = 30000m, IsActive = true });
            db.AttendanceRecords.AddRange(
                new AttendanceRecord { EmployeeId = 1, Year = now.Year, Month = now.Month, NetSalaryOverride = 7777m },
                new AttendanceRecord { EmployeeId = 2, Year = now.Year, Month = now.Month, BaseSalaryOverride = 20000m });
        });

        var viewModel = NewDashboardViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(30000m, viewModel.GrossPayroll);
        Assert.Equal(27777m, viewModel.PayablePayroll);
    }

    [Fact]
    public async Task DashboardSalaryChanges_UsesCurrentMonthDateRange()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var now = DateTime.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "A", BaseSalary = 10000m, IsActive = true });
            db.SalaryRevisions.AddRange(
                new SalaryRevision
                {
                    EmployeeId = 1,
                    OldSalary = 10000m,
                    NewSalary = 11000m,
                    ChangedAt = monthStart.AddSeconds(-1)
                },
                new SalaryRevision
                {
                    EmployeeId = 1,
                    OldSalary = 10000m,
                    NewSalary = 11200m,
                    ChangedAt = monthStart
                },
                new SalaryRevision
                {
                    EmployeeId = 1,
                    OldSalary = 10000m,
                    NewSalary = 11500m,
                    ChangedAt = nextMonthStart.AddSeconds(-1)
                },
                new SalaryRevision
                {
                    EmployeeId = 1,
                    OldSalary = 10000m,
                    NewSalary = 12000m,
                    ChangedAt = nextMonthStart
                });
        });

        var viewModel = NewDashboardViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.SalaryChangesCount);
        Assert.Equal(2700m, viewModel.TotalRevisionIncrease);
    }

    [Fact]
    public async Task SalarySheetLoad_CashPaymentOrCashGroupDisablesAndClearsStatutoryDeductions()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.EmployeeGroups.Add(new EmployeeGroup { Id = 1, Name = "Cash" });
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Cash Payment", BaseSalary = 20000m, PaymentMode = PaymentMode.Cash, IsActive = true },
                new Employee { Id = 2, Name = "Cash Group", BaseSalary = 30000m, PaymentMode = PaymentMode.OtherBank, IsActive = true },
                new Employee { Id = 3, Name = "Bank High", BaseSalary = 30000m, PaymentMode = PaymentMode.OtherBank, IsActive = true },
                new Employee { Id = 4, Name = "Bank Low", BaseSalary = 20000m, PaymentMode = PaymentMode.OtherBank, IsActive = true });
            db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership { EmployeeId = 2, EmployeeGroupId = 1 });
            db.AttendanceRecords.AddRange(
                Attendance(1, esic: 100m, pf: 200m, tds: 300m),
                Attendance(2, esic: 110m, pf: 210m, tds: 310m),
                Attendance(3, esic: 120m, pf: 220m, tds: 400m),
                Attendance(4, esic: 50m, pf: 60m, tds: 500m));
        });

        var viewModel = NewSalarySheetViewModel(factory, initializer);
        await viewModel.LoadCommand.ExecuteAsync(null);

        var cashPayment = viewModel.Rows.Single(r => r.Name == "Cash Payment");
        Assert.True(cashPayment.IsCashDeductionsDisabled);
        Assert.False(cashPayment.UsesEsicPf);
        Assert.False(cashPayment.UsesTds);
        Assert.Equal(0m, cashPayment.EsicDeduction);
        Assert.Equal(0m, cashPayment.PfDeduction);
        Assert.Equal(0m, cashPayment.TdsDeduction);

        var cashGroup = viewModel.Rows.Single(r => r.Name == "Cash Group");
        Assert.True(cashGroup.IsCashDeductionsDisabled);
        Assert.False(cashGroup.UsesEsicPf);
        Assert.False(cashGroup.UsesTds);
        Assert.Equal(0m, cashGroup.EsicDeduction);
        Assert.Equal(0m, cashGroup.PfDeduction);
        Assert.Equal(0m, cashGroup.TdsDeduction);

        Assert.Equal(400m, viewModel.Rows.Single(r => r.Name == "Bank High").TdsDeduction);
        Assert.Equal(50m, viewModel.Rows.Single(r => r.Name == "Bank Low").EsicDeduction);
        Assert.Equal(60m, viewModel.Rows.Single(r => r.Name == "Bank Low").PfDeduction);

        await viewModel.SaveAttendanceCommand.ExecuteAsync(null);

        using var verifyDb = factory.CreateDbContext();
        var saved = await verifyDb.AttendanceRecords.AsNoTracking().ToDictionaryAsync(a => a.EmployeeId);
        Assert.Equal(0m, saved[1].EsicDeduction);
        Assert.Equal(0m, saved[1].PfDeduction);
        Assert.Equal(0m, saved[1].TdsDeduction);
        Assert.Equal(0m, saved[2].EsicDeduction);
        Assert.Equal(0m, saved[2].PfDeduction);
        Assert.Equal(0m, saved[2].TdsDeduction);
        Assert.Equal(400m, saved[3].TdsDeduction);
        Assert.Equal(50m, saved[4].EsicDeduction);
        Assert.Equal(60m, saved[4].PfDeduction);
    }

    private static AttendanceRecord Attendance(int employeeId, decimal esic, decimal pf, decimal tds)
        => new()
        {
            EmployeeId = employeeId,
            Year = 2026,
            Month = 5,
            DaysAbsent = 0,
            EsicDeduction = esic,
            PfDeduction = pf,
            TdsDeduction = tds
        };

    private static EmployeesViewModel NewEmployeesViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
        => new(factory, initializer, NewDialogService(), new ExcelImportService());

    private static AttendanceViewModel NewAttendanceViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
        => new(factory, initializer, NewDialogService())
        {
            SelectedYear = 2026,
            SelectedMonth = SalaryManager.App.Helpers.Months.All.Single(m => m.Number == 5)
        };

    private static ReportsViewModel NewReportsViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
        => new(factory, initializer, new PdfSlipService(), new ExcelExportService(), new MonthlyPayrollService(factory), NewDialogService(), new AppSettingsService())
        {
            SelectedYear = 2026,
            SelectedMonth = SalaryManager.App.Helpers.Months.All.Single(m => m.Number == 5)
        };

    private static DashboardViewModel NewDashboardViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
        => new(factory, initializer, new BackupService(), NewDialogService(), new MonthlyPayrollService(factory));

    private static SalarySheetViewModel NewSalarySheetViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer,
        DialogService? dialogs = null)
    {
        var viewModel = new SalarySheetViewModel(
            factory,
            initializer,
            new MonthlyPayrollService(factory),
            new PdfSlipService(),
            new ExcelExportService(),
            dialogs ?? NewDialogService(),
            new AppSettingsService())
        {
            SelectedYear = 2026,
            SelectedMonth = SalaryManager.App.Helpers.Months.All.Single(m => m.Number == 5)
        };

        return viewModel;
    }

    private static DialogService NewDialogService()
    {
        var dialogs = Substitute.For<DialogService>();
        dialogs.ErrorAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        dialogs.InfoAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        dialogs.ConfirmDestructiveAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        dialogs.ConfirmWarningAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        dialogs.AskNonNegativeDecimalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>())
            .Returns(Task.FromResult<decimal?>(null));
        return dialogs;
    }

    private static async Task SeedAsync(SqliteDbContextFactory factory, Action<AppDbContext> seed)
    {
        using var db = factory.CreateDbContext();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static async Task<DatabaseInitializer> ReadyInitializerAsync(IDbContextFactory<AppDbContext> factory)
    {
        var initializer = new DatabaseInitializer();
        initializer.StartMigration(factory);
        await initializer.ReadyTask.WaitAsync(TimeSpan.FromSeconds(10));
        return initializer;
    }

    private sealed class SqliteDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public SqliteDbContextFactory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public AppDbContext CreateDbContext()
            => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());

        public void Dispose()
            => _connection.Dispose();
    }
}
