using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.Tests;

public class MonthlyPayrollServiceTests
{
    [Fact]
    public async Task LoadAsync_GroupFiltersIncludeAnySelectedMembership()
    {
        using var factory = new SqliteDbContextFactory();
        await SeedAsync(factory, db =>
        {
            db.EmployeeGroups.AddRange(
                new EmployeeGroup { Id = 1, Name = "Factory" },
                new EmployeeGroup { Id = 2, Name = "Night" });
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Both", BaseSalary = 10000m, IsActive = true },
                new Employee { Id = 2, Name = "Factory Only", BaseSalary = 10000m, IsActive = true },
                new Employee { Id = 3, Name = "Night Only", BaseSalary = 10000m, IsActive = true },
                new Employee { Id = 4, Name = "Inactive Factory", BaseSalary = 10000m, IsActive = false },
                new Employee { Id = 5, Name = "Ungrouped", BaseSalary = 10000m, IsActive = true });
            db.EmployeeGroupMemberships.AddRange(
                new EmployeeGroupMembership { EmployeeId = 1, EmployeeGroupId = 1 },
                new EmployeeGroupMembership { EmployeeId = 1, EmployeeGroupId = 2 },
                new EmployeeGroupMembership { EmployeeId = 2, EmployeeGroupId = 1 },
                new EmployeeGroupMembership { EmployeeId = 3, EmployeeGroupId = 2 },
                new EmployeeGroupMembership { EmployeeId = 4, EmployeeGroupId = 1 });
        });

        var service = new MonthlyPayrollService(factory);

        var activeSnapshot = await service.LoadAsync(new MonthlyPayrollRequest(
            2026,
            5,
            GroupIds: [2, 1, 1, 0, -1]));
        var allSnapshot = await service.LoadAsync(new MonthlyPayrollRequest(
            2026,
            5,
            GroupIds: [1],
            ActiveOnly: false));

        Assert.Equal(["Both", "Factory Only", "Night Only"], activeSnapshot.Rows.Select(r => r.Name));
        Assert.Equal(["Both", "Factory Only", "Inactive Factory"], allSnapshot.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task LoadAsync_CashPaymentOrCashGroupDisablesStatutoryDeductions()
    {
        using var factory = new SqliteDbContextFactory();
        await SeedAsync(factory, db =>
        {
            db.EmployeeGroups.Add(new EmployeeGroup { Id = 1, Name = "Cash" });
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Cash Payment", BaseSalary = 20000m, PaymentMode = PaymentMode.Cash },
                new Employee { Id = 2, Name = "Cash Group", BaseSalary = 30000m, PaymentMode = PaymentMode.OtherBank },
                new Employee { Id = 3, Name = "Bank High", BaseSalary = 30000m, PaymentMode = PaymentMode.OtherBank },
                new Employee { Id = 4, Name = "Bank Low", BaseSalary = 20000m, PaymentMode = PaymentMode.OtherBank });
            db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership { EmployeeId = 2, EmployeeGroupId = 1 });
            db.AttendanceRecords.AddRange(
                Attendance(1, esic: 100m, pf: 200m, tds: 300m),
                Attendance(2, esic: 110m, pf: 210m, tds: 310m),
                Attendance(3, esic: 120m, pf: 220m, tds: 400m),
                Attendance(4, esic: 50m, pf: 60m, tds: 500m));
        });

        var snapshot = await new MonthlyPayrollService(factory)
            .LoadAsync(new MonthlyPayrollRequest(2026, 5));

        var cashPayment = snapshot.Rows.Single(r => r.Name == "Cash Payment");
        Assert.True(cashPayment.IsCashDeductionsDisabled);
        Assert.False(cashPayment.UsesEsicPf);
        Assert.False(cashPayment.UsesTds);
        Assert.Equal(0m, cashPayment.EsicDeduction);
        Assert.Equal(0m, cashPayment.PfDeduction);
        Assert.Equal(0m, cashPayment.TdsDeduction);

        var cashGroup = snapshot.Rows.Single(r => r.Name == "Cash Group");
        Assert.True(cashGroup.IsCashDeductionsDisabled);
        Assert.False(cashGroup.UsesEsicPf);
        Assert.False(cashGroup.UsesTds);
        Assert.Equal(0m, cashGroup.EsicDeduction);
        Assert.Equal(0m, cashGroup.PfDeduction);
        Assert.Equal(0m, cashGroup.TdsDeduction);

        var bankHigh = snapshot.Rows.Single(r => r.Name == "Bank High");
        Assert.False(bankHigh.IsTdsManualOverride);
        Assert.Equal(3000m, bankHigh.TdsDeduction);
        Assert.Equal(0m, bankHigh.EsicDeduction);
        Assert.Equal(0m, bankHigh.PfDeduction);

        var bankLow = snapshot.Rows.Single(r => r.Name == "Bank Low");
        Assert.Equal(50m, bankLow.EsicDeduction);
        Assert.Equal(60m, bankLow.PfDeduction);
        Assert.Equal(0m, bankLow.TdsDeduction);
    }

    [Fact]
    public async Task LoadAsync_PreservesManualTdsOverride()
    {
        using var factory = new SqliteDbContextFactory();
        await SeedAsync(factory, db =>
        {
            db.Employees.Add(new Employee { Id = 1, Name = "Bank High", BaseSalary = 30000m, PaymentMode = PaymentMode.OtherBank });
            db.AttendanceRecords.Add(Attendance(1, esic: 0m, pf: 0m, tds: 400m, isTdsManualOverride: true));
        });

        var snapshot = await new MonthlyPayrollService(factory)
            .LoadAsync(new MonthlyPayrollRequest(2026, 5));

        var row = Assert.Single(snapshot.Rows);
        Assert.True(row.IsTdsManualOverride);
        Assert.Equal(400m, row.TdsDeduction);
        Assert.Equal(29600m, row.NetSalary);
    }

    [Fact]
    public async Task LoadAsync_AppliesSalaryPaidOverridesAndLegacyNetOverrides()
    {
        using var factory = new SqliteDbContextFactory();
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Legacy Net", BaseSalary = 10000m },
                new Employee { Id = 2, Name = "Base Override", BaseSalary = 30000m });
            db.AttendanceRecords.AddRange(
                new AttendanceRecord
                {
                    EmployeeId = 1,
                    Year = 2026,
                    Month = 5,
                    NetSalaryOverride = 7777m
                },
                new AttendanceRecord
                {
                    EmployeeId = 2,
                    Year = 2026,
                    Month = 5,
                    DaysAbsent = 1,
                    EsicDeduction = 100m,
                    PfDeduction = 200m,
                    TdsDeduction = 500m,
                    BaseSalaryOverride = 20000m,
                    NetSalaryOverride = 8888m
                });
        });

        var snapshot = await new MonthlyPayrollService(factory)
            .LoadAsync(new MonthlyPayrollRequest(2026, 5));

        var legacy = snapshot.Rows.Single(r => r.Name == "Legacy Net");
        Assert.Equal(10000m, legacy.EffectiveBaseSalary);
        Assert.Equal(7777m, legacy.HistoricalNetSalaryOverride);
        Assert.Equal(7777m, legacy.NetSalary);

        var salaryOverride = snapshot.Rows.Single(r => r.Name == "Base Override");
        Assert.Equal(30000m, salaryOverride.EmployeeBaseSalary);
        Assert.Equal(30000m, salaryOverride.EffectiveBaseSalary);
        Assert.Equal(20000m, salaryOverride.SalaryPaid);
        Assert.Null(salaryOverride.HistoricalNetSalaryOverride);
        Assert.True(salaryOverride.UsesEsicPf);
        Assert.False(salaryOverride.UsesTds);
        Assert.Equal(100m, salaryOverride.EsicDeduction);
        Assert.Equal(200m, salaryOverride.PfDeduction);
        Assert.Equal(0m, salaryOverride.TdsDeduction);
        Assert.Equal(19700m, salaryOverride.NetSalary);
    }

    [Fact]
    public async Task LoadAsync_UsesSalaryAdvanceDeductionsAndBalances()
    {
        using var factory = new SqliteDbContextFactory();
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Deducted", BaseSalary = 10000m },
                new Employee { Id = 2, Name = "Manual Only", BaseSalary = 10000m });
            db.Advances.AddRange(
                new Advance { EmployeeId = 1, Amount = 1000m, EntryType = AdvanceEntryType.Given, Date = new DateTime(2026, 5, 1) },
                new Advance { EmployeeId = 1, Amount = 200m, EntryType = AdvanceEntryType.Deducted, Date = new DateTime(2026, 5, 31), SourceKey = AdvanceSourceKeys.Salary(2026, 5) },
                new Advance { EmployeeId = 1, Amount = 50m, EntryType = AdvanceEntryType.Deducted, Date = new DateTime(2026, 4, 30), SourceKey = AdvanceSourceKeys.Salary(2026, 4) },
                new Advance { EmployeeId = 2, Amount = 300m, EntryType = AdvanceEntryType.Given, Date = new DateTime(2026, 5, 1) });
        });

        var snapshot = await new MonthlyPayrollService(factory)
            .LoadAsync(new MonthlyPayrollRequest(2026, 5));

        var deducted = snapshot.Rows.Single(r => r.Name == "Deducted");
        Assert.Equal(750m, deducted.AdvanceBalance);
        Assert.Equal(200m, deducted.AdvanceDeduction);
        Assert.Equal(10000m, deducted.NetSalaryBeforeAdvance);
        Assert.Equal(9800m, deducted.NetSalary);

        var manualOnly = snapshot.Rows.Single(r => r.Name == "Manual Only");
        Assert.Equal(300m, manualOnly.AdvanceBalance);
        Assert.Equal(0m, manualOnly.AdvanceDeduction);
        Assert.Equal(10000m, manualOnly.NetSalary);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptySnapshotForEmptyEmployeeSet()
    {
        using var factory = new SqliteDbContextFactory();

        var snapshot = await new MonthlyPayrollService(factory)
            .LoadAsync(new MonthlyPayrollRequest(2026, 5));

        Assert.Equal(2026, snapshot.Year);
        Assert.Equal(5, snapshot.Month);
        Assert.Empty(snapshot.Rows);
    }

    private static AttendanceRecord Attendance(int employeeId, decimal esic, decimal pf, decimal tds, bool isTdsManualOverride = false)
        => new()
        {
            EmployeeId = employeeId,
            Year = 2026,
            Month = 5,
            DaysAbsent = 0,
            EsicDeduction = esic,
            PfDeduction = pf,
            TdsDeduction = tds,
            IsTdsManualOverride = isTdsManualOverride
        };

    private static async Task SeedAsync(SqliteDbContextFactory factory, Action<AppDbContext> seed)
    {
        using var db = factory.CreateDbContext();
        seed(db);
        await db.SaveChangesAsync();
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

            using var db = CreateDbContext();
            db.Database.EnsureCreated();
        }

        public AppDbContext CreateDbContext()
            => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());

        public void Dispose()
            => _connection.Dispose();
    }
}
