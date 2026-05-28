using System.Linq;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data;
using SalaryManager.Data.Entities;

namespace SalaryManager.Tests;

public class AppDbContextIndexTests
{
    [Fact]
    public void AttendanceRecord_HasPayrollLookupIndex()
        => AssertHasIndex<AttendanceRecord>("Year", "Month", "EmployeeId");

    [Fact]
    public void Advance_HasSalaryDeductionLookupIndex()
        => AssertHasIndex<Advance>("SourceKey", "EntryType", "EmployeeId");

    [Fact]
    public void Advance_HasGlobalRecentIndex()
        => AssertHasIndex<Advance>("Date", "Id");

    [Fact]
    public void SalaryRevision_HasGlobalChangedAtIndex()
        => AssertHasIndex<SalaryRevision>("ChangedAt");

    private static void AssertHasIndex<TEntity>(params string[] propertyNames)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new AppDbContext(options);
        var entityType = db.Model.FindEntityType(typeof(TEntity));

        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.Properties.Select(p => p.Name).SequenceEqual(propertyNames));
    }
}
