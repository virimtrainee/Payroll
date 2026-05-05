using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class AdvanceQueryExtensionsTests
{
    private static AppDbContext NewInMemoryContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new AppDbContext(opts);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task SumBalancesByEmployee_MatchesAdvanceLedgerBalance()
    {
        using var db = NewInMemoryContext();
        db.Employees.AddRange(
            new Employee { Id = 1, Name = "A", BaseSalary = 1000 },
            new Employee { Id = 2, Name = "B", BaseSalary = 2000 });
        db.Advances.AddRange(
            new Advance { EmployeeId = 1, Amount = 2000m, EntryType = AdvanceEntryType.Given,    Date = new DateTime(2025, 1, 5) },
            new Advance { EmployeeId = 1, Amount =  500m, EntryType = AdvanceEntryType.Deducted, Date = new DateTime(2025, 2, 1) },
            new Advance { EmployeeId = 2, Amount = 1500m, EntryType = AdvanceEntryType.Given,    Date = new DateTime(2025, 1, 1) });
        await db.SaveChangesAsync();

        var result = await db.Advances.AsNoTracking().SumBalancesByEmployeeAsync();

        Assert.Equal(1500m, result[1]); // 2000 - 500
        Assert.Equal(1500m, result[2]);
    }

    [Fact]
    public async Task SumOutstanding_TotalsAcrossAllEmployees()
    {
        using var db = NewInMemoryContext();
        db.Employees.Add(new Employee { Id = 1, Name = "A" });
        db.Advances.AddRange(
            new Advance { EmployeeId = 1, Amount = 2000m, EntryType = AdvanceEntryType.Given,    Date = DateTime.Today },
            new Advance { EmployeeId = 1, Amount =  500m, EntryType = AdvanceEntryType.Deducted, Date = DateTime.Today });
        await db.SaveChangesAsync();

        var total = await db.Advances.AsNoTracking().SumOutstandingAsync();

        Assert.Equal(1500m, total);
    }

    [Fact]
    public async Task SumOutstanding_EmptyTable_ReturnsZero()
    {
        using var db = NewInMemoryContext();

        var total = await db.Advances.AsNoTracking().SumOutstandingAsync();

        Assert.Equal(0m, total);
    }
}
