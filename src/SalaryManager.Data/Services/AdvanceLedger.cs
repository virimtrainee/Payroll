using System.Collections.Generic;
using System.Linq;
using SalaryManager.Data.Entities;

namespace SalaryManager.Data.Services;

public record LedgerRow(Advance Entry, decimal RunningBalance);

public static class AdvanceLedger
{
    public static decimal Balance(IEnumerable<Advance> entries)
    {
        decimal bal = 0;
        foreach (var e in entries)
            bal += e.EntryType == AdvanceEntryType.Given ? e.Amount : -e.Amount;
        return bal;
    }

    public static IReadOnlyList<LedgerRow> Build(IEnumerable<Advance> entries)
    {
        var ordered = entries.OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();
        var rows = new List<LedgerRow>(ordered.Count);
        decimal bal = 0;
        foreach (var e in ordered)
        {
            bal += e.EntryType == AdvanceEntryType.Given ? e.Amount : -e.Amount;
            rows.Add(new LedgerRow(e, bal));
        }
        return rows;
    }
}
