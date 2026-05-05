using System;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class AdvanceLedgerTests
{
    [Fact]
    public void Balance_GivenMinusDeducted()
    {
        var entries = new[]
        {
            new Advance { Amount = 2000m, EntryType = AdvanceEntryType.Given,    Date = new DateTime(2025, 1, 5) },
            new Advance { Amount =  500m, EntryType = AdvanceEntryType.Deducted, Date = new DateTime(2025, 2, 1) },
            new Advance { Amount = 1000m, EntryType = AdvanceEntryType.Given,    Date = new DateTime(2025, 2, 15) },
        };

        Assert.Equal(2500m, AdvanceLedger.Balance(entries));
    }

    [Fact]
    public void Build_RunningBalanceMatchesChronology()
    {
        var entries = new[]
        {
            new Advance { Id = 1, Amount = 2000m, EntryType = AdvanceEntryType.Given,    Date = new DateTime(2025, 1, 5) },
            new Advance { Id = 2, Amount =  500m, EntryType = AdvanceEntryType.Deducted, Date = new DateTime(2025, 2, 1) },
        };

        var rows = AdvanceLedger.Build(entries);
        Assert.Equal(2000m, rows[0].RunningBalance);
        Assert.Equal(1500m, rows[1].RunningBalance);
    }
}
