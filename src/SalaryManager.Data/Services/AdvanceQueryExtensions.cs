using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data.Entities;

namespace SalaryManager.Data.Services;

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
}
