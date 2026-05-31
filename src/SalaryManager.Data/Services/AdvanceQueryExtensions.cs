using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data.Entities;

namespace SalaryManager.Data.Services;

internal sealed record AdvanceBalanceSummary(decimal Outstanding, int EmployeesWithPendingAdvances);

public static class AdvanceQueryExtensions
{
    public static Task<Dictionary<int, decimal>> SumBalancesByEmployeeAsync(
        this IQueryable<Advance> source, CancellationToken ct = default)
    {
        return source
            .GroupBy(a => a.EmployeeId)
            .Select(g => new
            {
                Id = g.Key,
                Bal = g.Sum(a =>
                    (a.EntryType == AdvanceEntryType.Given ? 1m : -1m) * a.Amount)
            })
            .ToDictionaryAsync(x => x.Id, x => x.Bal, ct);
    }

    public static Task<decimal> SumOutstandingAsync(
        this IQueryable<Advance> source, CancellationToken ct = default)
    {
        return source.SumAsync(a =>
            (a.EntryType == AdvanceEntryType.Given ? 1m : -1m) * a.Amount, ct);
    }

    internal static async Task<AdvanceBalanceSummary> SumBalanceSummaryAsync(
        this IQueryable<Advance> source, CancellationToken ct = default)
    {
        var summary = await source
            .GroupBy(a => a.EmployeeId)
            .Select(g => new
            {
                Balance = g.Sum(a =>
                    (a.EntryType == AdvanceEntryType.Given ? 1m : -1m) * a.Amount)
            })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Outstanding = g.Sum(x => x.Balance),
                EmployeesWithPendingAdvances = g.Count(x => x.Balance != 0m)
            })
            .SingleOrDefaultAsync(ct);

        return summary is null
            ? new AdvanceBalanceSummary(0m, 0)
            : new AdvanceBalanceSummary(summary.Outstanding, summary.EmployeesWithPendingAdvances);
    }
}
