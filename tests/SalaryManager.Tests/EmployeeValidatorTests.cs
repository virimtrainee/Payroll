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

    [Fact]
    public void Validate_OptionalIdentityFieldsOnlyEnforceMaxLength()
    {
        var valid = EmployeeValidator.Validate(new EmployeeValidationInput(
            "A",
            1000m,
            PaymentMode.Cash,
            null,
            null,
            AadharNumber: "1234 5678 9012",
            UanNumber: "UAN-1",
            InsuranceNumber: "INS-42",
            PhoneNumber: "+91 98765 43210"));

        var invalid = EmployeeValidator.Validate(new EmployeeValidationInput(
            "A",
            1000m,
            PaymentMode.Cash,
            null,
            null,
            PhoneNumber: new string('1', 31)));

        Assert.True(valid.IsValid);
        Assert.Contains(invalid.Issues, issue => issue.Code == "phone_too_long");
    }
}
