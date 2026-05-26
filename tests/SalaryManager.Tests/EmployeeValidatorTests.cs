using SalaryManager.Data.Entities;
using SalaryManager.Data.Validation;
using Xunit;

namespace SalaryManager.Tests;

public class EmployeeValidatorTests
{
    [Fact]
    public void Validate_RejectsMissingName()
    {
        var result = EmployeeValidator.Validate(new EmployeeValidationInput(
            " ",
            1000m,
            PaymentMode.Cash,
            null,
            null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "name_required");
    }

    [Fact]
    public void Validate_RejectsNegativeSalary()
    {
        var result = EmployeeValidator.Validate(new EmployeeValidationInput(
            "A",
            -1m,
            PaymentMode.Cash,
            null,
            null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "salary_negative");
    }

    [Fact]
    public void Validate_RejectsDuplicateNameCaseInsensitively()
    {
        var result = EmployeeValidator.Validate(
            new EmployeeValidationInput("alice", 1000m, PaymentMode.Cash, null, null),
            ["Alice"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "name_duplicate");
    }
}
