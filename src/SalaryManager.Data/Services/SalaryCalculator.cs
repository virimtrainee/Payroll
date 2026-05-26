using System;

namespace SalaryManager.Data.Services;

public record SalaryBreakdown(
    decimal BaseSalary,
    int DaysInMonth,
    int DaysAbsent,
    int DaysPresent,
    decimal PerDayRate,
    decimal Deduction,
    decimal EsicDeduction,
    decimal PfDeduction,
    decimal TdsDeduction,
    decimal NetSalary);

public static class SalaryCalculator
{
    public static SalaryBreakdown Compute(
        decimal baseSalary, int year, int month, int daysAbsent,
        decimal esicDeduction = 0, decimal pfDeduction = 0, decimal tdsDeduction = 0)
    {
        if (baseSalary < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSalary), "Base salary cannot be negative.");
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1-12.");

        var daysInMonth = DateTime.DaysInMonth(year, month);

        if (daysAbsent < 0)
            throw new ArgumentOutOfRangeException(nameof(daysAbsent), "Days absent cannot be negative.");
        if (daysAbsent > daysInMonth)
            daysAbsent = daysInMonth;

        if (baseSalary > 25000m)
        {
            esicDeduction = 0;
            pfDeduction = 0;
            if (tdsDeduction < 0) tdsDeduction = 0;
        }
        else
        {
            if (esicDeduction < 0) esicDeduction = 0;
            if (pfDeduction < 0) pfDeduction = 0;
            tdsDeduction = 0;
        }

        var perDay = decimal.Round(baseSalary / daysInMonth, 4, MidpointRounding.ToEven);
        var deduction = decimal.Round(perDay * daysAbsent, 2, MidpointRounding.AwayFromZero);
        var net = decimal.Round(baseSalary - deduction - esicDeduction - pfDeduction - tdsDeduction, 2, MidpointRounding.AwayFromZero);

        return new SalaryBreakdown(
            BaseSalary: baseSalary,
            DaysInMonth: daysInMonth,
            DaysAbsent: daysAbsent,
            DaysPresent: daysInMonth - daysAbsent,
            PerDayRate: perDay,
            Deduction: deduction,
            EsicDeduction: esicDeduction,
            PfDeduction: pfDeduction,
            TdsDeduction: tdsDeduction,
            NetSalary: net);
    }
}
