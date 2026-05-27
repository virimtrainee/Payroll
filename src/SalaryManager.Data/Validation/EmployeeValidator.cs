using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
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
        var context = new EmployeeValidationContext(input, existingNames, rowNumber);
        var fluentResult = EmployeeFluentValidator.Instance.Validate(context);
        return new ValidationResult(fluentResult.Errors.Select(ToIssue));
    }

    private sealed record EmployeeValidationContext(
        EmployeeValidationInput Input,
        IEnumerable<string>? ExistingNames,
        int? RowNumber);

    private sealed class EmployeeFluentValidator : AbstractValidator<EmployeeValidationContext>
    {
        public static EmployeeFluentValidator Instance { get; } = new();

        private EmployeeFluentValidator()
        {
            RuleFor(x => x)
                .Custom((context, validation) =>
                {
                    var input = context.Input;
                    var rowNumber = context.RowNumber;
                    var name = input.Name?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(name))
                        validation.AddIssue("Employee name", "Employee name is required.", "name_required", rowNumber);
                    else if (name.Length > 120)
                        validation.AddIssue("Employee name", "Employee name cannot exceed 120 characters.", "name_too_long", rowNumber);

                    if (context.ExistingNames?.Any(n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)) == true)
                        validation.AddIssue("Employee name", "An employee with this name already exists.", "name_duplicate", rowNumber);

                    if (input.BaseSalary < 0)
                        validation.AddIssue("Base salary", "Base salary cannot be negative.", "salary_negative", rowNumber);

                    if (!Enum.IsDefined(input.PaymentMode))
                        validation.AddIssue("Payment mode", "Payment mode is not valid.", "payment_mode_invalid", rowNumber);

                    if (input.JoiningDate?.Date > DateTime.Today)
                        validation.AddIssue("Joining date", "Joining date cannot be in the future.", "joining_date_future", rowNumber);

                    if (input.PaymentMode == PaymentMode.Cash)
                        return;

                    var account = input.AccountNumber?.Trim() ?? string.Empty;
                    var ifsc = input.IfscCode?.Trim().ToUpperInvariant() ?? string.Empty;

                    if (!AccountNumberRegex().IsMatch(account))
                        validation.AddIssue("Account number", "Account number must contain 6 to 30 digits.", "account_invalid", rowNumber);

                    if (!IfscRegex().IsMatch(ifsc))
                        validation.AddIssue("IFSC", "IFSC must match the format ABCD0XXXXXX.", "ifsc_invalid", rowNumber);
                });
        }
    }

    private static ValidationIssue ToIssue(ValidationFailure failure)
        => new(
            failure.PropertyName,
            failure.ErrorMessage,
            failure.ErrorCode,
            failure.CustomState as int?);

    private static void AddIssue(
        this ValidationContext<EmployeeValidationContext> validation,
        string field,
        string message,
        string code,
        int? rowNumber)
        => validation.AddFailure(new ValidationFailure(field, message)
        {
            ErrorCode = code,
            CustomState = rowNumber
        });

    [GeneratedRegex(@"^\d{6,30}$")]
    private static partial Regex AccountNumberRegex();

    [GeneratedRegex(@"^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex IfscRegex();
}
