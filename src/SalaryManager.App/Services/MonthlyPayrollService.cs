using Microsoft.EntityFrameworkCore;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using SalaryManager.Data.Validation;

namespace SalaryManager.App.Services;

public sealed record MonthlyPayrollRequest(
    int Year,
    int Month,
    IReadOnlyCollection<int>? GroupIds = null,
    bool ActiveOnly = true);

public sealed record MonthlyPayrollGroup(int Id, string Name);

public sealed record MonthlyPayrollRow(
    int EmployeeId,
    string Name,
    decimal EmployeeBaseSalary,
    decimal EffectiveBaseSalary,
    decimal SalaryPaid,
    DateTime? JoiningDate,
    string? AccountNumber,
    string? IfscCode,
    PaymentMode PaymentMode,
    IReadOnlyList<MonthlyPayrollGroup> Groups,
    string PrimaryGroupName,
    bool IsCashDeductionsDisabled,
    int DaysAbsent,
    decimal EsicDeduction,
    decimal PfDeduction,
    decimal TdsDeduction,
    bool IsTdsManualOverride,
    decimal AdvanceDeduction,
    decimal AdvanceBalance,
    decimal? HistoricalNetSalaryOverride,
    SalaryBreakdown? Breakdown,
    IReadOnlyList<ValidationIssue> ValidationIssues)
{
    public bool UsesTds => !IsCashDeductionsDisabled && SalaryPaid > SalaryCalculator.TdsThreshold;
    public bool UsesEsicPf => !IsCashDeductionsDisabled && !UsesTds;
    public decimal NetSalaryBeforeAdvance => Breakdown?.NetSalary ?? 0m;
    public decimal NetSalary => HistoricalNetSalaryOverride ?? NetSalaryBeforeAdvance - AdvanceDeduction;

    public MonthlySummaryRow ToMonthlySummaryRow()
        => new(
            Name,
            EffectiveBaseSalary,
            SalaryPaid,
            DaysAbsent,
            Breakdown?.Deduction ?? 0m,
            Breakdown?.EsicDeduction ?? 0m,
            Breakdown?.PfDeduction ?? 0m,
            Breakdown?.TdsDeduction ?? 0m,
            AdvanceDeduction,
            NetSalary);
}

public sealed record MonthlyPayrollSnapshot(
    int Year,
    int Month,
    IReadOnlyList<MonthlyPayrollRow> Rows);

public class MonthlyPayrollService
{
    private static readonly string CashGroupNormalizedName = GroupNameNormalizer.Normalize("Cash");

    private readonly IDbContextFactory<AppDbContext> _dbf;

    public MonthlyPayrollService(IDbContextFactory<AppDbContext> dbf)
    {
        _dbf = dbf;
    }

    public async Task<MonthlyPayrollSnapshot> LoadAsync(
        MonthlyPayrollRequest request,
        CancellationToken ct = default)
    {
        using var db = await _dbf.CreateDbContextAsync(ct);

        var selectedGroupIds = request.GroupIds?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? [];

        var employeeQuery = db.Employees.AsNoTracking()
            .Include(e => e.GroupMemberships)
                .ThenInclude(m => m.EmployeeGroup)
            .AsSplitQuery()
            .AsQueryable();

        if (request.ActiveOnly)
            employeeQuery = employeeQuery.Where(e => e.IsActive);

        if (selectedGroupIds.Count > 0)
        {
            employeeQuery = employeeQuery.Where(e =>
                e.GroupMemberships.Any(m => selectedGroupIds.Contains(m.EmployeeGroupId)));
        }

        var employees = await employeeQuery
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

        if (employees.Count == 0)
            return new MonthlyPayrollSnapshot(request.Year, request.Month, []);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var sourceKey = AdvanceSourceKeys.Salary(request.Year, request.Month);

        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                     && a.Year == request.Year
                     && a.Month == request.Month)
            .ToDictionaryAsync(a => a.EmployeeId, a => a, ct);

        var balances = await db.Advances.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId))
            .SumBalancesByEmployeeAsync(ct);

        var salaryAdvanceDeductions = await db.Advances.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                     && a.SourceKey == sourceKey
                     && a.EntryType == AdvanceEntryType.Deducted)
            .ToDictionaryAsync(a => a.EmployeeId, a => a.Amount, ct);

        var rows = new List<MonthlyPayrollRow>(employees.Count);
        foreach (var employee in employees)
        {
            attendance.TryGetValue(employee.Id, out var record);
            var groups = employee.GroupMemberships
                .Select(m => m.EmployeeGroup)
                .Where(g => g is not null)
                .Select(g => new MonthlyPayrollGroup(g!.Id, g.Name))
                .OrderBy(g => g.Name)
                .ToList();

            var effectiveBaseSalary = employee.BaseSalary;
            var daysAbsent = record?.DaysAbsent ?? 0;
            var defaultSalaryPaid = SalaryCalculator.CalculateDefaultSalaryPaid(
                effectiveBaseSalary,
                request.Year,
                request.Month,
                daysAbsent);
            var salaryPaid = record?.BaseSalaryOverride is decimal salaryOverride
                ? SalaryCalculator.RoundSalaryPaid(salaryOverride)
                : defaultSalaryPaid;
            var isCashDeductionsDisabled =
                employee.PaymentMode == PaymentMode.Cash
                || groups.Any(g => GroupNameNormalizer.Normalize(g.Name) == CashGroupNormalizedName);
            var usesTds = !isCashDeductionsDisabled && salaryPaid > SalaryCalculator.TdsThreshold;
            var usesEsicPf = !isCashDeductionsDisabled && !usesTds;
            var esic = usesEsicPf ? record?.EsicDeduction ?? 0m : 0m;
            var pf = usesEsicPf ? record?.PfDeduction ?? 0m : 0m;
            var isTdsManualOverride = usesTds && record?.IsTdsManualOverride == true;
            var tds = usesTds
                ? isTdsManualOverride
                    ? record?.TdsDeduction ?? SalaryCalculator.CalculateDefaultTds(salaryPaid)
                    : SalaryCalculator.CalculateDefaultTds(salaryPaid)
                : 0m;
            var advanceDeduction = salaryAdvanceDeductions.GetValueOrDefault(employee.Id, 0m);
            var advanceBalance = balances.GetValueOrDefault(employee.Id, 0m);
            var historicalNetOverride = record?.BaseSalaryOverride is null
                ? record?.NetSalaryOverride
                : null;

            var validation = PayrollValidator.Validate(new PayrollValidationInput(
                employee.Name,
                effectiveBaseSalary,
                request.Year,
                request.Month,
                daysAbsent,
                esic,
                pf,
                tds,
                advanceDeduction,
                advanceBalance,
                advanceDeduction,
                salaryPaid));
            var issues = validation.Issues.ToList();
            if (historicalNetOverride < 0)
                issues.Add(new ValidationIssue(employee.Name, "Net salary override cannot be negative.", "net_override_negative"));

            SalaryBreakdown? breakdown = null;
            if (issues.Count == 0)
            {
                breakdown = SalaryCalculator.ComputeFromSalaryPaid(
                    effectiveBaseSalary,
                    salaryPaid,
                    request.Year,
                    request.Month,
                    daysAbsent,
                    esic,
                    pf,
                    tds);
            }

            rows.Add(new MonthlyPayrollRow(
                employee.Id,
                employee.Name,
                employee.BaseSalary,
                effectiveBaseSalary,
                salaryPaid,
                employee.JoiningDate,
                employee.AccountNumber,
                employee.IfscCode,
                employee.PaymentMode,
                groups,
                groups.FirstOrDefault()?.Name ?? "Other",
                isCashDeductionsDisabled,
                daysAbsent,
                esic,
                pf,
                tds,
                isTdsManualOverride,
                advanceDeduction,
                advanceBalance,
                historicalNetOverride,
                breakdown,
                issues));
        }

        return new MonthlyPayrollSnapshot(request.Year, request.Month, rows);
    }
}
