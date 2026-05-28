using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using SalaryManager.Data.Validation;

namespace SalaryManager.App.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly PdfSlipService _pdf;
    private readonly ExcelExportService _excel;
    private readonly MonthlyPayrollService _monthlyPayroll;
    private readonly DialogService _dialogs;
    private readonly AppSettingsService _settings;

    public List<MonthOption> Months => Helpers.Months.All;
    public List<int> Years => Helpers.Months.Years();

    [ObservableProperty] private MonthOption selectedMonth = Helpers.Months.All[DateTime.Now.Month - 1];
    [ObservableProperty] private int selectedYear = DateTime.Now.Year;

    public RangeObservableCollection<Employee> Employees { get; } = new();
    public RangeObservableCollection<GroupFilterOptionVm> GroupFilterOptions { get; } = new();
    [ObservableProperty] private Employee? selectedEmployee;
    [ObservableProperty] private GroupFilterOptionVm? selectedGroupFilter;

    [ObservableProperty] private string kpiTotalPayable = "₹0.00";
    [ObservableProperty] private string kpiGross = "₹0.00";
    [ObservableProperty] private string kpiDeductions = "₹0.00";
    [ObservableProperty] private string kpiTotalAdvances = "₹0.00";
    [ObservableProperty] private int kpiActiveEmployees;
    [ObservableProperty] private int revisionEntryCount;
    [ObservableProperty] private int revisionEmployeeCount;
    [ObservableProperty] private string lastRevisionDate = "—";
    [ObservableProperty] private bool isLoading;
    private bool _updatingGroupFilter;
    private int _loadVersion;
    private int _employeeLoadVersion;
    private int _kpiLoadVersion;
    private static readonly string CashGroupNormalizedName = GroupNameNormalizer.Normalize("Cash");

    public ReportsViewModel(IDbContextFactory<AppDbContext> dbf,
                            DatabaseInitializer dbInit,
                            PdfSlipService pdf,
                            ExcelExportService excel,
                            MonthlyPayrollService monthlyPayroll,
                            DialogService dialogs,
                            AppSettingsService settings)
    {
        _dbf = dbf; _dbInit = dbInit; _pdf = pdf; _excel = excel; _monthlyPayroll = monthlyPayroll; _dialogs = dialogs; _settings = settings;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        try
        {
            await _dbInit.ReadyTask;
            using (var db = await _dbf.CreateDbContextAsync())
                await LoadGroupFiltersAsync(db);
            await LoadEmployeesAsync();
            await LoadKpisAsync();
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                await _dialogs.ErrorAsync(ex.Message);
        }
        finally
        {
            if (version == _loadVersion)
                IsLoading = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadEmployeesAsync()
    {
        var version = Interlocked.Increment(ref _employeeLoadVersion);
        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var query = db.Employees.AsNoTracking();
            if (SelectedGroupFilter?.Id is int groupId)
                query = query.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

            var list = await query.OrderBy(e => e.Name).ToListAsync();
            if (version != _employeeLoadVersion) return;
            Employees.ReplaceAll(list);
            SelectedEmployee = Employees.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (version == _employeeLoadVersion)
                await _dialogs.ErrorAsync(ex.Message);
        }
    }

    partial void OnSelectedMonthChanged(MonthOption value) => LoadKpisCommand.Execute(null);
    partial void OnSelectedYearChanged(int value) => LoadKpisCommand.Execute(null);
    partial void OnSelectedGroupFilterChanged(GroupFilterOptionVm? value)
    {
        if (_updatingGroupFilter) return;
        LoadEmployeesCommand.Execute(null);
        LoadKpisCommand.Execute(null);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadKpisAsync()
    {
        var version = Interlocked.Increment(ref _kpiLoadVersion);
        try
        {
            var snapshot = await LoadCurrentPayrollSnapshotAsync();
            var rows = BuildValidatedSummaryRows(snapshot);
            var gross = rows.Sum(r => r.SalaryPaid);
            var net = rows.Sum(r => r.NetSalary);
            var ded = gross - net;
            var active = snapshot.Rows.Count;

            using var db = await _dbf.CreateDbContextAsync();
            var advanceQuery = db.Advances.AsNoTracking();
            var revisionQuery = db.SalaryRevisions.AsNoTracking();
            if (SelectedGroupFilter?.Id is int filterGroupId)
            {
                advanceQuery = advanceQuery.Where(a => a.Employee != null
                    && a.Employee.GroupMemberships.Any(m => m.EmployeeGroupId == filterGroupId));
                revisionQuery = revisionQuery.Where(r => r.Employee.GroupMemberships.Any(m => m.EmployeeGroupId == filterGroupId));
            }

            var advances = await advanceQuery.SumOutstandingAsync();
            var revisionCount = await revisionQuery.CountAsync();
            var revisionEmployees = await revisionQuery.Select(r => r.EmployeeId).Distinct().CountAsync();
            var lastRevision = await revisionQuery
                .OrderByDescending(r => r.ChangedAt)
                .Select(r => (DateTime?)r.ChangedAt)
                .FirstOrDefaultAsync();

            if (version != _kpiLoadVersion) return;
            KpiTotalPayable = FormatCurrency(net);
            KpiGross = FormatCurrency(gross);
            KpiDeductions = FormatCurrency(ded);
            KpiTotalAdvances = FormatCurrency(advances);
            KpiActiveEmployees = active;
            RevisionEntryCount = revisionCount;
            RevisionEmployeeCount = revisionEmployees;
            LastRevisionDate = lastRevision?.ToString("dd MMM yyyy") ?? "—";
        }
        catch (Exception ex)
        {
            if (version == _kpiLoadVersion)
                await _dialogs.ErrorAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task SummaryPdfAsync()
    {
        try
        {
            var rows = await BuildSummaryRowsAsync();
            var name = $"Payroll-{SelectedYear:0000}-{SelectedMonth.Number:00}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", name);
            if (path is null) return;
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _pdf.GenerateMonthlySummary(year, month, rows, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task SummaryExcelAsync()
    {
        try
        {
            var rows = await BuildSummaryRowsAsync();
            var name = $"Payroll-{SelectedYear:0000}-{SelectedMonth.Number:00}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", name);
            if (path is null) return;
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _excel.ExportMonthlySummary(year, month, rows, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task SummaryCsvAsync()
    {
        try
        {
            var rows = await BuildSummaryRowsAsync();
            var name = $"Payroll-{SelectedYear:0000}-{SelectedMonth.Number:00}.csv";
            var path = _dialogs.AskSavePath("CSV (*.csv)|*.csv", name);
            if (path is null) return;
            await Task.Run(() =>
            {
                using var w = new StreamWriter(path);
                using var csv = new CsvWriter(w, CultureInfo.InvariantCulture);
                csv.WriteRecords(rows);
            });
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task IciciExcelAsync()
    {
        try
        {
            var rows = await BuildIciciRowsAsync();
            if (rows.Count == 0) { await _dialogs.InfoAsync("No bank-payment employees in this period."); return; }

            var name = $"ICICI-Salary-{SelectedYear:0000}-{SelectedMonth.Number:00}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", name);
            if (path is null) return;

            var settings = _settings.Load();
            var options = await _dialogs.AskIciciExportOptionsAsync(settings.IciciDebitAccountNo);
            if (options is null) return;
            _settings.Save(settings with { IciciDebitAccountNo = options.DebitAccountNo });

            await Task.Run(() => _excel.ExportIciciPayment(rows, options, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task LedgerPdfAsync()
    {
        try
        {
            if (SelectedEmployee is null) { await _dialogs.ErrorAsync("Pick an employee."); return; }
            var (rows, bal) = await BuildLedgerAsync(SelectedEmployee.Id);
            var name = $"AdvanceLedger-{Sanitize(SelectedEmployee.Name)}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", name);
            if (path is null) return;
            var emp = SelectedEmployee;
            await Task.Run(() => _pdf.GenerateAdvanceLedger(emp, rows, bal, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task LedgerExcelAsync()
    {
        try
        {
            if (SelectedEmployee is null) { await _dialogs.ErrorAsync("Pick an employee."); return; }
            var (rows, bal) = await BuildLedgerAsync(SelectedEmployee.Id);
            var name = $"AdvanceLedger-{Sanitize(SelectedEmployee.Name)}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", name);
            if (path is null) return;
            var emp = SelectedEmployee;
            await Task.Run(() => _excel.ExportAdvanceLedger(emp, rows, bal, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task SalarySlipPdfAsync()
    {
        try
        {
            if (SelectedEmployee is null) { await _dialogs.ErrorAsync("Pick an employee."); return; }
            var data = await BuildSalarySlipDataAsync(SelectedEmployee.Id);
            var name = $"SalarySlip-{Sanitize(SelectedEmployee.Name)}-{SelectedYear:0000}-{SelectedMonth.Number:00}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", name);
            if (path is null) return;
            await Task.Run(() => _pdf.GenerateSlip(data, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task SalarySlipPrintAsync()
    {
        try
        {
            if (SelectedEmployee is null) { await _dialogs.ErrorAsync("Pick an employee."); return; }
            var data = await BuildSalarySlipDataAsync(SelectedEmployee.Id);
            var path = await Task.Run(() => _pdf.GenerateSlip(data));
            PrintFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task RevisionPdfAsync()
    {
        try
        {
            var rows = await BuildRevisionRowsAsync();
            var name = $"Salary-Revisions-{DateTime.Now:yyyyMMdd}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", name);
            if (path is null) return;
            await Task.Run(() => _pdf.GenerateSalaryRevisionReport(rows, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task RevisionExcelAsync()
    {
        try
        {
            var rows = await BuildRevisionRowsAsync();
            var name = $"Salary-Revisions-{DateTime.Now:yyyyMMdd}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", name);
            if (path is null) return;
            await Task.Run(() => _excel.ExportSalaryRevisions(rows, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    private async Task<List<MonthlySummaryRow>> BuildSummaryRowsAsync()
    {
        var snapshot = await LoadCurrentPayrollSnapshotAsync();
        return BuildValidatedSummaryRows(snapshot);
    }

    private async Task<List<IciciPaymentRow>> BuildIciciRowsAsync()
    {
        var snapshot = await LoadCurrentPayrollSnapshotAsync();
        var paymentRows = snapshot.Rows
            .Where(r => r.PaymentMode != PaymentMode.Cash)
            .ToList();
        var issues = new List<ValidationIssue>();
        var rows = new List<IciciPaymentRow>(paymentRows.Count);
        foreach (var row in paymentRows)
        {
            issues.AddRange(row.ValidationIssues);

            var employeeValidation = EmployeeValidator.Validate(new EmployeeValidationInput(
                row.Name,
                row.EmployeeBaseSalary,
                row.PaymentMode,
                row.AccountNumber,
                row.IfscCode,
                row.JoiningDate));
            issues.AddRange(employeeValidation.Issues.Select(i => i with { Field = $"{row.Name} - {i.Field}" }));
            if (row.ValidationIssues.Count > 0)
                continue;

            var amount = row.NetSalary;
            if (amount <= 0)
                issues.Add(new ValidationIssue(row.Name, "ICICI export amount must be greater than zero."));

            rows.Add(new IciciPaymentRow(row.Name, row.AccountNumber, row.IfscCode, amount, row.PaymentMode));
        }

        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        return rows;
    }

    private Task<MonthlyPayrollSnapshot> LoadCurrentPayrollSnapshotAsync()
    {
        IReadOnlyCollection<int>? groupIds = SelectedGroupFilter?.Id is int groupId
            ? new[] { groupId }
            : null;

        return _monthlyPayroll.LoadAsync(new MonthlyPayrollRequest(
            SelectedYear,
            SelectedMonth.Number,
            groupIds));
    }

    private static List<MonthlySummaryRow> BuildValidatedSummaryRows(MonthlyPayrollSnapshot snapshot)
    {
        var issues = snapshot.Rows.SelectMany(r => r.ValidationIssues).ToList();
        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        return snapshot.Rows
            .Select(r => r.ToMonthlySummaryRow())
            .ToList();
    }

    private async Task<(IReadOnlyList<LedgerRow> Rows, decimal Balance)> BuildLedgerAsync(int employeeId)
    {
        using var db = await _dbf.CreateDbContextAsync();
        var entries = await db.Advances.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.Date).ThenBy(a => a.Id)
            .ToListAsync();
        var rows = AdvanceLedger.Build(entries);
        return (rows, rows.LastOrDefault()?.RunningBalance ?? 0m);
    }

    private async Task<SalarySlipData> BuildSalarySlipDataAsync(int employeeId)
    {
        using var db = await _dbf.CreateDbContextAsync();
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.GroupMemberships)
                .ThenInclude(m => m.EmployeeGroup)
            .SingleOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null)
            throw new InvalidOperationException("Selected employee was not found.");

        var attendance = await db.AttendanceRecords.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId
                                   && a.Year == SelectedYear
                                   && a.Month == SelectedMonth.Number);
        var sourceKey = AdvanceSourceKeys.Salary(SelectedYear, SelectedMonth.Number);
        var advanceDeduction = await db.Advances.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId
                     && a.SourceKey == sourceKey
                     && a.EntryType == AdvanceEntryType.Deducted)
            .SumAsync(a => (decimal?)a.Amount) ?? 0m;
        var advanceBalance = await db.Advances.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .SumOutstandingAsync();

        var daysAbsent = attendance?.DaysAbsent ?? 0;
        var effectiveBaseSalary = employee.BaseSalary;
        var salaryPaid = EffectiveSalaryPaid(employee, attendance, daysAbsent, SelectedYear, SelectedMonth.Number);
        var esic = UsesEsicPf(employee, salaryPaid) ? attendance?.EsicDeduction ?? 0m : 0m;
        var pf = UsesEsicPf(employee, salaryPaid) ? attendance?.PfDeduction ?? 0m : 0m;
        var tds = UsesTds(employee, salaryPaid) ? attendance?.TdsDeduction ?? 0m : 0m;

        var validation = PayrollValidator.Validate(new PayrollValidationInput(
            employee.Name,
            effectiveBaseSalary,
            SelectedYear,
            SelectedMonth.Number,
            daysAbsent,
            esic,
            pf,
            tds,
            advanceDeduction,
            advanceBalance,
            advanceDeduction,
            salaryPaid));
        var issues = validation.Issues.ToList();
        var slipNetOverride = EffectiveHistoricalNetSalaryOverride(attendance);
        if (slipNetOverride < 0)
            issues.Add(new ValidationIssue(employee.Name, "Net salary override cannot be negative.", "net_override_negative"));
        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        var breakdown = SalaryCalculator.ComputeFromSalaryPaid(
            effectiveBaseSalary,
            salaryPaid,
            SelectedYear,
            SelectedMonth.Number,
            daysAbsent,
            esic,
            pf,
            tds);
        return new SalarySlipData(
            employee,
            SelectedYear,
            SelectedMonth.Number,
            breakdown,
            advanceBalance,
            advanceDeduction,
            slipNetOverride);
    }

    private async Task<List<SalaryRevisionReportRow>> BuildRevisionRowsAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();
        IQueryable<SalaryRevision> query = db.SalaryRevisions.AsNoTracking().Include(r => r.Employee);
        if (SelectedGroupFilter?.Id is int groupId)
            query = query.Where(r => r.Employee.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

        return await query
            .OrderByDescending(r => r.ChangedAt)
            .ThenBy(r => r.Employee.Name)
            .Select(r => new SalaryRevisionReportRow(
                r.Employee.Name,
                r.OldSalary,
                r.NewSalary,
                r.ChangedAt,
                r.Note))
            .ToListAsync();
    }

    private static string Sanitize(string s) => string.Join("_", s.Split(Path.GetInvalidFileNameChars()));

    private static string FormatCurrency(decimal value) =>
        $"₹{value.ToString("N0", CultureInfo.GetCultureInfo("en-IN"))}";

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private static void PrintFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" }); }
        catch { OpenFile(path); }
    }

    private static decimal EffectiveSalaryPaid(Employee employee, AttendanceRecord? attendance, int daysAbsent, int year, int month)
        => attendance?.BaseSalaryOverride
        ?? SalaryCalculator.CalculateDefaultSalaryPaid(employee.BaseSalary, year, month, daysAbsent);

    private static decimal? EffectiveHistoricalNetSalaryOverride(AttendanceRecord? attendance)
        => attendance?.BaseSalaryOverride is null ? attendance?.NetSalaryOverride : null;

    private static bool UsesEsicPf(Employee employee, decimal salaryPaid)
        => !HasCashDeductionsDisabled(employee) && salaryPaid <= 25000m;

    private static bool UsesTds(Employee employee, decimal salaryPaid)
        => !HasCashDeductionsDisabled(employee) && salaryPaid > 25000m;

    private static bool HasCashDeductionsDisabled(Employee employee)
        => employee.PaymentMode == PaymentMode.Cash
        || employee.GroupMemberships.Any(m =>
            m.EmployeeGroup is not null
            && GroupNameNormalizer.Normalize(m.EmployeeGroup.Name) == CashGroupNormalizedName);

    private async Task LoadGroupFiltersAsync(AppDbContext db)
    {
        var selectedId = SelectedGroupFilter?.Id;
        var groups = await db.EmployeeGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupFilterOptionVm(g.Id, g.Name))
            .ToListAsync();

        var options = new List<GroupFilterOptionVm> { new(null, "All Groups") };
        options.AddRange(groups);

        _updatingGroupFilter = true;
        try
        {
            GroupFilterOptions.ReplaceAll(options);
            SelectedGroupFilter = options.FirstOrDefault(g => g.Id == selectedId) ?? options[0];
        }
        finally
        {
            _updatingGroupFilter = false;
        }
    }
}
