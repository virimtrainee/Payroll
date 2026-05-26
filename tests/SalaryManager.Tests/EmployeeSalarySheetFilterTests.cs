using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    public async Task SalarySheetLoad_SelectedGroupsIncludeAnyMembership()
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

        viewModel.GroupFilterOptions.Single(g => g.Name == "Factory").IsSelected = true;
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.GroupFilterOptions.Single(g => g.Name == "Night").IsSelected = true;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Both", "Factory Only", "Night Only"], viewModel.Rows.Select(r => r.Name));
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
        => new(factory, initializer, new CapturingDialogService(), new ExcelImportService());

    private static SalarySheetViewModel NewSalarySheetViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
    {
        var viewModel = new SalarySheetViewModel(
            factory,
            initializer,
            new PdfSlipService(),
            new ExcelExportService(),
            new CapturingDialogService(),
            new AppSettingsService())
        {
            SelectedYear = 2026,
            SelectedMonth = SalaryManager.App.Helpers.Months.All.Single(m => m.Number == 5)
        };

        return viewModel;
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

    private sealed class CapturingDialogService : DialogService
    {
        public List<string> Errors { get; } = [];
        public List<string> Infos { get; } = [];

        public override void Error(string message, string title = "Error")
            => Errors.Add(message);

        public override void Info(string message, string title = "Information")
            => Infos.Add(message);
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
