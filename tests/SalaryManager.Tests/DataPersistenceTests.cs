using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class DataPersistenceTests
{
    private static AppDbContext NewSqliteContext()
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
    public async Task EmployeeGroupName_IsUniqueAfterNormalization()
    {
        using var db = NewSqliteContext();
        db.EmployeeGroups.Add(new EmployeeGroup { Name = "Factory Staff" });
        await db.SaveChangesAsync();

        db.EmployeeGroups.Add(new EmployeeGroup { Name = " factory   staff " });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateGroupMembership_IsRejected()
    {
        using var db = NewSqliteContext();
        db.Employees.Add(new Employee { Id = 1, Name = "A" });
        db.EmployeeGroups.Add(new EmployeeGroup { Id = 1, Name = "Factory" });
        await db.SaveChangesAsync();

        db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership { EmployeeId = 1, EmployeeGroupId = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.EmployeeGroupMemberships.Add(new EmployeeGroupMembership { EmployeeId = 1, EmployeeGroupId = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AdvanceSourceKey_AllowsMultipleManualRowsButRejectsDuplicateSalarySource()
    {
        using var db = NewSqliteContext();
        db.Employees.Add(new Employee { Id = 1, Name = "A" });
        db.Advances.AddRange(
            new Advance { EmployeeId = 1, Amount = 100m, EntryType = AdvanceEntryType.Given, Date = DateTime.Today },
            new Advance { EmployeeId = 1, Amount = 200m, EntryType = AdvanceEntryType.Given, Date = DateTime.Today });
        await db.SaveChangesAsync();

        var sourceKey = AdvanceSourceKeys.Salary(2026, 5);
        db.Advances.AddRange(
            new Advance { EmployeeId = 1, Amount = 100m, EntryType = AdvanceEntryType.Deducted, Date = DateTime.Today, SourceKey = sourceKey },
            new Advance { EmployeeId = 1, Amount = 200m, EntryType = AdvanceEntryType.Deducted, Date = DateTime.Today, SourceKey = sourceKey });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AttendanceRecord_PersistsTdsDeduction()
    {
        using var db = NewSqliteContext();
        db.Employees.Add(new Employee { Id = 1, Name = "A" });
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = 1,
            Year = 2026,
            Month = 5,
            DaysAbsent = 0,
            TdsDeduction = 750m
        });
        await db.SaveChangesAsync();

        var saved = await db.AttendanceRecords.AsNoTracking().SingleAsync();

        Assert.Equal(750m, saved.TdsDeduction);
    }

    [Fact]
    public async Task AttendanceRecord_PersistsNetSalaryOverride()
    {
        using var db = NewSqliteContext();
        db.Employees.Add(new Employee { Id = 1, Name = "A" });
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = 1,
            Year = 2026,
            Month = 5,
            DaysAbsent = 0,
            NetSalaryOverride = 8750m
        });
        await db.SaveChangesAsync();

        var saved = await db.AttendanceRecords.AsNoTracking().SingleAsync();

        Assert.Equal(8750m, saved.NetSalaryOverride);
    }
}
