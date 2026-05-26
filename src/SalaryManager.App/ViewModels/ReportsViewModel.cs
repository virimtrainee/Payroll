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
    [ObservableProperty] private int kpiActiveEmployees;
    [ObservableProperty] private bool isLoading;
    private bool _updatingGroupFilter;
    private int _loadVersion;
    private int _employeeLoadVersion;
    private int _kpiLoadVersion;

    public ReportsViewModel(IDbContextFactory<AppDbContext> dbf,
                            DatabaseInitializer dbInit,
                            PdfSlipService pdf,
                            ExcelExportService excel,
                            DialogService dialogs)
    {
        _dbf = dbf; _dbInit = dbInit; _pdf = pdf; _excel = excel; _dialogs = dialogs;
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
                _dialogs.Error(ex.Message);
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
                _dialogs.Error(ex.Message);
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

            if (version != _kpiLoadVersion) return;
            KpiTotalPayable = $"₹{net:N0}";
            KpiGross = $"₹{gross:N0}";
            KpiDeductions = $"₹{ded:N0}";
            KpiActiveEmployees = active;
        }
        catch (Exception ex)
        {
            if (version == _kpiLoadVersion)
                _dialogs.Error(ex.Message);
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
        catch (Exception ex) { _dialogs.Error(ex.Message); }
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
        catch (Exception ex) { _dialogs.Error(ex.Message); }
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
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task LedgerPdfAsync()
    {
        try
        {
            if (SelectedEmployee is null) { _dialogs.Error("Pick an employee."); return; }
            var (rows, bal) = await BuildLedgerAsync(SelectedEmployee.Id);
            var name = $"AdvanceLedger-{Sanitize(SelectedEmployee.Name)}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", name);
            if (path is null) return;
            var emp = SelectedEmployee;
            await Task.Run(() => _pdf.GenerateAdvanceLedger(emp, rows, bal, path));
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task LedgerExcelAsync()
    {
        try
        {
            if (SelectedEmployee is null) { _dialogs.Error("Pick an employee."); return; }
            var (rows, bal) = await BuildLedgerAsync(SelectedEmployee.Id);
            var name = $"AdvanceLedger-{Sanitize(SelectedEmployee.Name)}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", name);
            if (path is null) return;
            var emp = SelectedEmployee;
            await Task.Run(() => _excel.ExportAdvanceLedger(emp, rows, bal, path));
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
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

            var b = SalaryCalculator.Compute(e.BaseSalary, SelectedYear, SelectedMonth.Number, absent, esic, pf, tds);
            list.Add(new MonthlySummaryRow(e.Name, e.BaseSalary, absent, b.Deduction,
                b.EsicDeduction, b.PfDeduction, b.TdsDeduction, advanceDeduction,
                b.NetSalary - advanceDeduction));
        }

        if (issues.Count > 0)
            throw new InvalidOperationException(new ValidationResult(issues).ToMessage());

        return list;
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

    private static string Sanitize(string s) => string.Join("_", s.Split(Path.GetInvalidFileNameChars()));

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

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
