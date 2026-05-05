using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    [ObservableProperty] private Employee? selectedEmployee;

    [ObservableProperty] private string kpiTotalPayable   = "₹0.00";
    [ObservableProperty] private string kpiGross          = "₹0.00";
    [ObservableProperty] private string kpiDeductions     = "₹0.00";
    [ObservableProperty] private int    kpiActiveEmployees;

    public ReportsViewModel(IDbContextFactory<AppDbContext> dbf,
                            DatabaseInitializer dbInit,
                            PdfSlipService pdf,
                            ExcelExportService excel,
                            DialogService dialogs)
    {
        _dbf = dbf; _dbInit = dbInit; _pdf = pdf; _excel = excel; _dialogs = dialogs;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _dbInit.ReadyTask;
        await LoadEmployeesAsync();
        await LoadKpisAsync();
    }

    private async Task LoadEmployeesAsync()
    {
        using var db = await _dbf.CreateDbContextAsync();
        var list = await db.Employees.AsNoTracking().OrderBy(e => e.Name).ToListAsync();
        Employees.ReplaceAll(list);
        SelectedEmployee = Employees.FirstOrDefault();
    }

    partial void OnSelectedMonthChanged(MonthOption value) => _ = LoadKpisAsync();
    partial void OnSelectedYearChanged(int value)          => _ = LoadKpisAsync();

    private async Task LoadKpisAsync()
    {
        try
        {
            var rows = await BuildSummaryRowsAsync();
            var gross = rows.Sum(r => r.BaseSalary);
            var net   = rows.Sum(r => r.NetSalary);
            var ded   = gross - net;

            using var db = await _dbf.CreateDbContextAsync();
            var active = await db.Employees.CountAsync(e => e.IsActive);

            KpiTotalPayable   = $"₹{net:N0}";
            KpiGross          = $"₹{gross:N0}";
            KpiDeductions     = $"₹{ded:N0}";
            KpiActiveEmployees = active;
        }
        catch { /* swallow on empty DB */ }
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
        var employees = await db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync();
        var ids = employees.Select(e => e.Id).ToList();
        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.Year == SelectedYear && a.Month == SelectedMonth.Number)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);

        var list = new List<MonthlySummaryRow>(employees.Count);
        foreach (var e in employees)
        {
            var rec = attendance.GetValueOrDefault(e.Id);
            var absent = rec?.DaysAbsent ?? 0;
            var esic = rec?.EsicDeduction ?? 0m;
            var pf = rec?.PfDeduction ?? 0m;
            var b = SalaryCalculator.Compute(e.BaseSalary, SelectedYear, SelectedMonth.Number, absent, esic, pf);
            list.Add(new MonthlySummaryRow(e.Name, e.BaseSalary, absent, b.Deduction, esic, pf, b.NetSalary));
        }
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
}
