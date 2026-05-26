using SalaryManager.Data.Validation;
using Xunit;

namespace SalaryManager.Tests;

public class PayrollValidatorTests
{
    [Fact]
    public void Validate_RejectsDaysAbsentAboveMonth()
    {
        var result = PayrollValidator.Validate(new PayrollValidationInput(
            "A",
            10000m,
            2026,
            2,
            30,
            0m,
            0m,
            0m,
            0m,
            0m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "days_absent_too_high");
    }

    [Fact]
    public void Validate_RejectsTdsForEsicPfSalary()
    {
        var result = PayrollValidator.Validate(new PayrollValidationInput(
            "A",
            25000m,
            2026,
            5,
            0,
            0m,
            0m,
            100m,
            0m,
            0m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "tds_not_allowed");
    }

    [Fact]
    public void Validate_RejectsAdvanceAboveAvailableBalance()
    {
        var result = PayrollValidator.Validate(new PayrollValidationInput(
            "A",
            10000m,
            2026,
            5,
            0,
            0m,
            0m,
            0m,
            600m,
            500m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "advance_exceeds_balance");
    }

    [Fact]
    public void Validate_AllowsCurrentPostedDeductionInAvailableBalance()
    {
        var result = PayrollValidator.Validate(new PayrollValidationInput(
            "A",
            10000m,
            2026,
            5,
            0,
            0m,
            0m,
            0m,
            600m,
            500m,
            100m));

        Assert.True(result.IsValid);
    }
}
