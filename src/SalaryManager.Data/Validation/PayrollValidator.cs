using FluentValidation;
using FluentValidation.Results;

namespace SalaryManager.Data.Validation;

public sealed record PayrollValidationInput(
    string EmployeeName,
    decimal BaseSalary,
    int Year,
    int Month,
    int DaysAbsent,
    decimal EsicDeduction,
    decimal PfDeduction,
    decimal TdsDeduction,
    decimal AdvanceDeduction,
    decimal AdvanceBalance,
    decimal ExistingSalaryAdvanceDeduction = 0);

public static class PayrollValidator
{
    public static ValidationResult Validate(PayrollValidationInput input)
    {
        var issues = PayrollBaseFluentValidator.Instance
            .Validate(input)
            .Errors
            .Select(ToIssue)
            .ToList();

        if (input.Month is < 1 or > 12)
            return new ValidationResult(issues);

        issues.AddRange(PayrollDeductionFluentValidator.Instance
            .Validate(input)
            .Errors
            .Select(ToIssue));

        if (issues.Count > 0)
            return new ValidationResult(issues);

        var breakdown = SalaryManager.Data.Services.SalaryCalculator.Compute(
            input.BaseSalary,
            input.Year,
            input.Month,
            input.DaysAbsent,
            input.EsicDeduction,
            input.PfDeduction,
            input.TdsDeduction);

        if (breakdown.NetSalary < 0)
            issues.Add(CreateIssue(input, "Deductions cannot exceed salary before advance deduction.", "deductions_exceed_salary"));

        if (input.AdvanceDeduction > breakdown.NetSalary)
            issues.Add(CreateIssue(input, "Advance deduction cannot exceed net salary before advance deduction.", "advance_exceeds_net"));

        var availableAdvance = Math.Max(0, input.AdvanceBalance) + Math.Max(0, input.ExistingSalaryAdvanceDeduction);
        if (input.AdvanceDeduction > availableAdvance)
            issues.Add(CreateIssue(input, "Advance deduction cannot exceed the available advance balance.", "advance_exceeds_balance"));

        return new ValidationResult(issues);
    }

    private sealed class PayrollBaseFluentValidator : AbstractValidator<PayrollValidationInput>
    {
        public static PayrollBaseFluentValidator Instance { get; } = new();

        private PayrollBaseFluentValidator()
        {
            RuleFor(x => x)
                .Custom((input, validation) =>
                {
                    if (input.BaseSalary < 0)
                        validation.AddIssue(input, "Base salary cannot be negative.", "salary_negative");

                    if (input.Month is < 1 or > 12)
                        validation.AddIssue(input, "Month must be between 1 and 12.", "month_invalid");
                });
        }
    }

    private sealed class PayrollDeductionFluentValidator : AbstractValidator<PayrollValidationInput>
    {
        public static PayrollDeductionFluentValidator Instance { get; } = new();

        private PayrollDeductionFluentValidator()
        {
            RuleFor(x => x)
                .Custom((input, validation) =>
                {
                    var daysInMonth = DateTime.DaysInMonth(input.Year, input.Month);
                    if (input.DaysAbsent is < 0)
                        validation.AddIssue(input, "Days absent cannot be negative.", "days_absent_negative");
                    else if (input.DaysAbsent > daysInMonth)
                        validation.AddIssue(input, $"Days absent cannot exceed {daysInMonth}.", "days_absent_too_high");

                    if (input.EsicDeduction < 0)
                        validation.AddIssue(input, "ESIC deduction cannot be negative.", "esic_negative");
                    if (input.PfDeduction < 0)
                        validation.AddIssue(input, "PF deduction cannot be negative.", "pf_negative");
                    if (input.TdsDeduction < 0)
                        validation.AddIssue(input, "TDS deduction cannot be negative.", "tds_negative");
                    if (input.AdvanceDeduction < 0)
                        validation.AddIssue(input, "Advance deduction cannot be negative.", "advance_negative");

                    var usesTds = input.BaseSalary > 25000m;
                    if (usesTds && (input.EsicDeduction > 0 || input.PfDeduction > 0))
                        validation.AddIssue(input, "ESIC/PF deductions are only allowed for salaries up to 25000.", "esic_pf_not_allowed");
                    if (!usesTds && input.TdsDeduction > 0)
                        validation.AddIssue(input, "TDS deduction is only allowed for salaries above 25000.", "tds_not_allowed");
                });
        }
    }

    private static ValidationIssue ToIssue(ValidationFailure failure)
        => new(failure.PropertyName, failure.ErrorMessage, failure.ErrorCode);

    private static ValidationIssue CreateIssue(PayrollValidationInput input, string message, string code)
        => new(GetPrefix(input), message, code);

    private static void AddIssue(
        this ValidationContext<PayrollValidationInput> validation,
        PayrollValidationInput input,
        string message,
        string code)
        => validation.AddFailure(new ValidationFailure(GetPrefix(input), message)
        {
            ErrorCode = code
        });

    private static string GetPrefix(PayrollValidationInput input)
        => string.IsNullOrWhiteSpace(input.EmployeeName) ? "Payroll" : input.EmployeeName;
}
