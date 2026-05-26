using System.Text.RegularExpressions;
using SalaryManager.Data.Entities;

namespace SalaryManager.Data.Validation;

public sealed record EmployeeValidationInput(
    string? Name,
    decimal BaseSalary,
    PaymentMode PaymentMode,
    string? AccountNumber,
    string? IfscCode,
    DateTime? JoiningDate = null)
{
    public EmployeeValidationInput(
        string? name,
        decimal baseSalary,
        string? accountNumber,
        string? ifscCode,
        PaymentMode paymentMode,
        DateTime? joiningDate = null)
        : this(name, baseSalary, paymentMode, accountNumber, ifscCode, joiningDate)
    {
    }
}

public static partial class EmployeeValidator
{
    public static ValidationResult Validate(
        EmployeeValidationInput input,
        IEnumerable<string>? existingNames = null,
        int? rowNumber = null)
    {
        var issues = new List<ValidationIssue>();
        var name = input.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
            issues.Add(new("Employee name", "Employee name is required.", "name_required", rowNumber));
        else if (name.Length > 120)
            issues.Add(new("Employee name", "Employee name cannot exceed 120 characters.", "name_too_long", rowNumber));

        if (existingNames?.Any(n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)) == true)
            issues.Add(new("Employee name", "An employee with this name already exists.", "name_duplicate", rowNumber));

        if (input.BaseSalary < 0)
            issues.Add(new("Base salary", "Base salary cannot be negative.", "salary_negative", rowNumber));

        if (!Enum.IsDefined(input.PaymentMode))
            issues.Add(new("Payment mode", "Payment mode is not valid.", "payment_mode_invalid", rowNumber));

        if (input.JoiningDate?.Date > DateTime.Today)
            issues.Add(new("Joining date", "Joining date cannot be in the future.", "joining_date_future", rowNumber));

        if (input.PaymentMode != PaymentMode.Cash)
        {
            var account = input.AccountNumber?.Trim() ?? string.Empty;
            var ifsc = input.IfscCode?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!AccountNumberRegex().IsMatch(account))
                issues.Add(new("Account number", "Account number must contain 6 to 30 digits.", "account_invalid", rowNumber));

            if (!IfscRegex().IsMatch(ifsc))
                issues.Add(new("IFSC", "IFSC must match the format ABCD0XXXXXX.", "ifsc_invalid", rowNumber));
        }

        return new ValidationResult(issues);
    }

    [GeneratedRegex(@"^\d{6,30}$")]
    private static partial Regex AccountNumberRegex();

    [GeneratedRegex(@"^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex IfscRegex();
}
