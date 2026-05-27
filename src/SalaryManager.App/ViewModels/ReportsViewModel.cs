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
                            DialogService dialogs,
                            AppSettingsService settings)
    {
        _dbf = dbf; _dbInit = dbInit; _pdf = pdf; _excel = excel; _dialogs = dialogs; _settings = settings;
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
            var rows = await BuildSummaryRowsAsync();
            var gross = rows.Sum(r => r.BaseSalary);
            var net = rows.Sum(r => r.NetSalary);
            var ded = gross - net;

            using var db = await _dbf.CreateDbContextAsync();
            var activeQuery = db.Employees.Where(e => e.IsActive);
            if (SelectedGroupFilter?.Id is int groupId)
                activeQuery = activeQuery.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));
            var active = await activeQuery.CountAsync();

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
        using var db = await _dbf.CreateDbContextAsync();
        var employeeQuery = db.Employees.AsNoTracking().Where(e => e.IsActive);
        if (SelectedGroupFilter?.Id is int groupId)
            employeeQuery = employeeQuery.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

        var employees = await employeeQuery.OrderBy(e => e.Name).ToListAsync();
        var ids = employees.Select(e => e.Id).ToList();
        var sourceKey = AdvanceSourceKeys.Salary(SelectedYear, SelectedMonth.Number);
        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.Year == SelectedYear && a.Month == SelectedMonth.Number)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);

        var advanceDeductions = await db.Advances.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId)
                     && a.SourceKey == sourceKey
                     && a.EntryType == AdvanceEntryType.Deducted)
            .ToDictionaryAsync(a => a.EmployeeId, a => a.Amount);

        var balances = await db.Advances.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId))
            .SumBalancesByEmployeeAsync();

        var list = new List<MonthlySummaryRow>(employees.Count);
        var issues = new List<ValidationIssue>();
        foreach (var e in employees)
        {
            var rec = attendance.GetValueOrDefault(e.Id);
            var absent = rec?.DaysAbsent ?? 0;
            var esic = rec?.EsicDeduction ?? 0m;
            var pf = rec?.PfDeduction ?? 0m;
            var tds = rec?.TdsDeduction ?? 0m;
            var advanceDeduction = advanceDeductions.GetValueOrDefault(e.Id, 0m);
            var validation = PayrollValidator.Validate(new PayrollValidationInput(
                e.Name,
                e.BaseSalary,
                SelectedYear,
                SelectedMonth.Number,
                absent,
                esic,
                pf,
                tds,
                advanceDeduction,
                balances.GetValueOrDefault(e.Id, 0m),
                advanceDeduction));
            issues.AddRange(validation.Issues);
            if (rec?.NetSalaryOverride < 0)
                issues.Add(new ValidationIssue(e.Name, "Net salary override cannot be negative.", "net_override_negative"));

            var b = SalaryCalculator.Compute(e.BaseSalary, SelectedYear, SelectedMonth.Number, absent, esic, pf, tds);
            var finalNetSalary = rec?.NetSalaryOverride ?? b.NetSalary - advanceDeduction;
            list.Add(new MonthlySummaryRow(e.Name, e.BaseSalary, absent, b.Deduction,
                b.EsicDeduction, b.PfDeduction, b.TdsDeduction, advanceDeduction,
                finalNetSalary));
        }

        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        return list;
    }

    private async Task<List<IciciPaymentRow>> BuildIciciRowsAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();
        var employeeQuery = db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.PaymentMode != PaymentMode.Cash);
        if (SelectedGroupFilter?.Id is int groupId)
            employeeQuery = employeeQuery.Where(e => e.GroupMemberships.Any(m => m.EmployeeGroupId == groupId));

        var employees = await employeeQuery.OrderBy(e => e.Name).ToListAsync();
        var ids = employees.Select(e => e.Id).ToList();
        var sourceKey = AdvanceSourceKeys.Salary(SelectedYear, SelectedMonth.Number);

        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.Year == SelectedYear && a.Month == SelectedMonth.Number)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);

        var advanceDeductions = await db.Advances.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId)
                     && a.SourceKey == sourceKey
                     && a.EntryType == AdvanceEntryType.Deducted)
            .ToDictionaryAsync(a => a.EmployeeId, a => a.Amount);

        var balances = await db.Advances.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId))
            .SumBalancesByEmployeeAsync();

        var issues = new List<ValidationIssue>();
        var rows = new List<IciciPaymentRow>(employees.Count);
        foreach (var e in employees)
        {
            var rec = attendance.GetValueOrDefault(e.Id);
            var advanceDeduction = advanceDeductions.GetValueOrDefault(e.Id, 0m);
            var payrollValidation = PayrollValidator.Validate(new PayrollValidationInput(
                e.Name,
                e.BaseSalary,
                SelectedYear,
                SelectedMonth.Number,
                rec?.DaysAbsent ?? 0,
                rec?.EsicDeduction ?? 0m,
                rec?.PfDeduction ?? 0m,
                rec?.TdsDeduction ?? 0m,
                advanceDeduction,
                balances.GetValueOrDefault(e.Id, 0m),
                advanceDeduction));
            issues.AddRange(payrollValidation.Issues);

            var employeeValidation = EmployeeValidator.Validate(new EmployeeValidationInput(
                e.Name,
                e.BaseSalary,
                e.PaymentMode,
                e.AccountNumber,
                e.IfscCode,
                e.JoiningDate));
            issues.AddRange(employeeValidation.Issues.Select(i => i with { Field = $"{e.Name} - {i.Field}" }));

            var b = SalaryCalculator.Compute(
                e.BaseSalary,
                SelectedYear,
                SelectedMonth.Number,
                rec?.DaysAbsent ?? 0,
                rec?.EsicDeduction ?? 0m,
                rec?.PfDeduction ?? 0m,
                rec?.TdsDeduction ?? 0m);
            var amount = rec?.NetSalaryOverride ?? b.NetSalary - advanceDeduction;
            if (amount <= 0)
                issues.Add(new ValidationIssue(e.Name, "ICICI export amount must be greater than zero."));

            rows.Add(new IciciPaymentRow(e.Name, e.AccountNumber, e.IfscCode, amount, e.PaymentMode));
        }

        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        return rows;
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
        var esic = UsesEsicPf(employee) ? attendance?.EsicDeduction ?? 0m : 0m;
        var pf = UsesEsicPf(employee) ? attendance?.PfDeduction ?? 0m : 0m;
        var tds = UsesTds(employee) ? attendance?.TdsDeduction ?? 0m : 0m;

        var validation = PayrollValidator.Validate(new PayrollValidationInput(
            employee.Name,
            employee.BaseSalary,
            SelectedYear,
            SelectedMonth.Number,
            daysAbsent,
            esic,
            pf,
            tds,
            advanceDeduction,
            advanceBalance,
            advanceDeduction));
        var issues = validation.Issues.ToList();
        if (attendance?.NetSalaryOverride < 0)
            issues.Add(new ValidationIssue(employee.Name, "Net salary override cannot be negative.", "net_override_negative"));
        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        var breakdown = SalaryCalculator.Compute(employee.BaseSalary, SelectedYear, SelectedMonth.Number, daysAbsent, esic, pf, tds);
        return new SalarySlipData(
            employee,
            SelectedYear,
            SelectedMonth.Number,
            breakdown,
            advanceBalance,
            advanceDeduction,
            attendance?.NetSalaryOverride);
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

    private static bool UsesEsicPf(Employee employee)
        => !HasCashDeductionsDisabled(employee) && employee.BaseSalary <= 25000m;

    private static bool UsesTds(Employee employee)
        => !HasCashDeductionsDisabled(employee) && employee.BaseSalary > 25000m;

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
