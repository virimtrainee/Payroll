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
        var issues = new List<ValidationIssue>();
        var prefix = string.IsNullOrWhiteSpace(input.EmployeeName) ? "Payroll" : input.EmployeeName;

        if (input.BaseSalary < 0)
            issues.Add(new(prefix, "Base salary cannot be negative.", "salary_negative"));

        if (input.Month is < 1 or > 12)
        {
            issues.Add(new(prefix, "Month must be between 1 and 12.", "month_invalid"));
            return new ValidationResult(issues);
        }

        var daysInMonth = DateTime.DaysInMonth(input.Year, input.Month);
        if (input.DaysAbsent is < 0)
            issues.Add(new(prefix, "Days absent cannot be negative.", "days_absent_negative"));
        else if (input.DaysAbsent > daysInMonth)
            issues.Add(new(prefix, $"Days absent cannot exceed {daysInMonth}.", "days_absent_too_high"));

        if (input.EsicDeduction < 0)
            issues.Add(new(prefix, "ESIC deduction cannot be negative.", "esic_negative"));
        if (input.PfDeduction < 0)
            issues.Add(new(prefix, "PF deduction cannot be negative.", "pf_negative"));
        if (input.TdsDeduction < 0)
            issues.Add(new(prefix, "TDS deduction cannot be negative.", "tds_negative"));
        if (input.AdvanceDeduction < 0)
            issues.Add(new(prefix, "Advance deduction cannot be negative.", "advance_negative"));

        var usesTds = input.BaseSalary > 25000m;
        if (usesTds && (input.EsicDeduction > 0 || input.PfDeduction > 0))
            issues.Add(new(prefix, "ESIC/PF deductions are only allowed for salaries up to 25000.", "esic_pf_not_allowed"));
        if (!usesTds && input.TdsDeduction > 0)
            issues.Add(new(prefix, "TDS deduction is only allowed for salaries above 25000.", "tds_not_allowed"));

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
            issues.Add(new(prefix, "Deductions cannot exceed salary before advance deduction.", "deductions_exceed_salary"));

        if (input.AdvanceDeduction > breakdown.NetSalary)
            issues.Add(new(prefix, "Advance deduction cannot exceed net salary before advance deduction.", "advance_exceeds_net"));

        var availableAdvance = Math.Max(0, input.AdvanceBalance) + Math.Max(0, input.ExistingSalaryAdvanceDeduction);
        if (input.AdvanceDeduction > availableAdvance)
            issues.Add(new(prefix, "Advance deduction cannot exceed the available advance balance.", "advance_exceeds_balance"));

        return new ValidationResult(issues);
    }
}
