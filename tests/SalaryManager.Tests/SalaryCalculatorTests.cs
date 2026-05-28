using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class SalaryCalculatorTests
{
    [Fact]
    public void NoAbsence_ReturnsFullBaseSalary()
    {
        var r = SalaryCalculator.Compute(30000m, 2025, 6, daysAbsent: 0);
        Assert.Equal(30000m, r.NetSalary);
        Assert.Equal(0m, r.Deduction);
        Assert.Equal(30, r.DaysInMonth);
        Assert.Equal(30, r.DaysPresent);
    }

    [Fact]
    public void ThreeAbsentInThirtyDayMonth_DeductsThreeDays()
    {
        var r = SalaryCalculator.Compute(30000m, 2025, 6, daysAbsent: 3);
        Assert.Equal(1000m, r.PerDayRate);
        Assert.Equal(3000m, r.Deduction);
        Assert.Equal(27000m, r.SalaryPaid);
        Assert.Equal(27000m, r.NetSalary);
    }

    [Fact]
    public void ComputeFromSalaryPaid_DoesNotApplyAbsenceTwice()
    {
        var r = SalaryCalculator.ComputeFromSalaryPaid(30000m, 28000m, 2025, 6, daysAbsent: 3,
            tdsDeduction: 100m);

        Assert.Equal(3000m, r.Deduction);
        Assert.Equal(28000m, r.SalaryPaid);
        Assert.Equal(27900m, r.NetSalary);
    }

    [Fact]
    public void ComputeFromSalaryPaid_UsesSalaryPaidForStatutoryThreshold()
    {
        var r = SalaryCalculator.ComputeFromSalaryPaid(30000m, 24000m, 2025, 6, daysAbsent: 3,
            esicDeduction: 100m, pfDeduction: 200m, tdsDeduction: 300m);

        Assert.Equal(100m, r.EsicDeduction);
        Assert.Equal(200m, r.PfDeduction);
        Assert.Equal(0m, r.TdsDeduction);
        Assert.Equal(23700m, r.NetSalary);
    }

    [Fact]
    public void FullMonthAbsent_NetSalaryIsZero()
    {
        var r = SalaryCalculator.Compute(31000m, 2025, 1, daysAbsent: 31);
        Assert.Equal(0m, r.NetSalary);
        Assert.Equal(0, r.DaysPresent);
    }

    [Fact]
    public void ThirtyOneDayMonth_UsesCorrectDivisor()
    {
        var r = SalaryCalculator.Compute(31000m, 2025, 1, daysAbsent: 1);
        Assert.Equal(31, r.DaysInMonth);
        Assert.Equal(1000m, r.PerDayRate);
        Assert.Equal(30000m, r.NetSalary);
    }

    [Fact]
    public void FebruaryNonLeap_HasTwentyEightDays()
    {
        var r = SalaryCalculator.Compute(28000m, 2025, 2, daysAbsent: 0);
        Assert.Equal(28, r.DaysInMonth);
        Assert.Equal(28000m, r.NetSalary);
    }

    [Fact]
    public void FebruaryLeapYear_HasTwentyNineDays()
    {
        var r = SalaryCalculator.Compute(29000m, 2024, 2, daysAbsent: 0);
        Assert.Equal(29, r.DaysInMonth);
    }

    [Fact]
    public void DaysAbsentExceedingMonth_ClampsToMonth()
    {
        var r = SalaryCalculator.Compute(30000m, 2025, 6, daysAbsent: 99);
        Assert.Equal(30, r.DaysAbsent);
        Assert.Equal(0m, r.NetSalary);
    }

    [Fact]
    public void SalaryAtThreshold_UsesEsicPfAndIgnoresTds()
    {
        var r = SalaryCalculator.Compute(25000m, 2025, 6, daysAbsent: 0,
            esicDeduction: 100m, pfDeduction: 200m, tdsDeduction: 300m);

        Assert.Equal(100m, r.EsicDeduction);
        Assert.Equal(200m, r.PfDeduction);
        Assert.Equal(0m, r.TdsDeduction);
        Assert.Equal(24700m, r.NetSalary);
    }

    [Fact]
    public void SalaryAboveThreshold_UsesTdsAndIgnoresEsicPf()
    {
        var r = SalaryCalculator.Compute(25000.01m, 2025, 6, daysAbsent: 0,
            esicDeduction: 100m, pfDeduction: 200m, tdsDeduction: 300m);

        Assert.Equal(0m, r.EsicDeduction);
        Assert.Equal(0m, r.PfDeduction);
        Assert.Equal(300m, r.TdsDeduction);
        Assert.Equal(24700.01m, r.NetSalary);
    }
}
