using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.App.ViewModels;

public partial class SalaryRowVm : ObservableObject
{
    public int      SerialNumber  { get; init; }
    public int      EmployeeId    { get; init; }
    public string   Name          { get; init; } = string.Empty;
    public int      ProRataDays   { get; init; }
    public decimal BaseSalary { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal AdvanceBalance { get; init; }
    public string? AccountNumber { get; init; }
    public string? IfscCode { get; init; }
    public PaymentMode PaymentMode { get; init; }

    // Editable attendance fields — trigger recalculation on change
    [ObservableProperty] private int daysAbsent;
    [ObservableProperty] private decimal esicDeduction;
    [ObservableProperty] private decimal pfDeduction;
    [ObservableProperty] private decimal advanceDeductionEntry;

    // Computed display fields — updated by Recalculate()
    [ObservableProperty] private decimal perDay;
    [ObservableProperty] private decimal deduction;
    [ObservableProperty] private decimal netSalary;

    partial void OnDaysAbsentChanged(int value)            => Recalculate();
    partial void OnEsicDeductionChanged(decimal value)     => Recalculate();
    partial void OnPfDeductionChanged(decimal value)       => Recalculate();
    partial void OnAdvanceDeductionEntryChanged(decimal value) => Recalculate();

    public void Recalculate()
    {
        var b = SalaryCalculator.Compute(BaseSalary, Year, Month,
            Math.Max(0, DaysAbsent), Math.Max(0, EsicDeduction), Math.Max(0, PfDeduction));
        PerDay    = b.PerDayRate;
        Deduction = b.Deduction;
        NetSalary = b.NetSalary - Math.Max(0, AdvanceDeductionEntry);
    }
}

public partial class SalarySheetViewModel : ObservableObject
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
    [ObservableProperty] private decimal totalNet;
    [ObservableProperty] private decimal totalGross;
    [ObservableProperty] private decimal totalDeduction;
    [ObservableProperty] private int employeeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridReadOnly))]
    [NotifyPropertyChangedFor(nameof(EditButtonText))]
    private bool isEditMode;

    [ObservableProperty] private bool isLoading;

    public bool IsGridReadOnly => !IsEditMode;
    public string EditButtonText => IsEditMode ? "Done" : "Edit";

    public RangeObservableCollection<SalaryRowVm> Rows { get; } = new();

    public SalarySheetViewModel(IDbContextFactory<AppDbContext> dbf,
                                DatabaseInitializer dbInit,
                                PdfSlipService pdf,
                                ExcelExportService excel,
                                DialogService dialogs)
    {
        _dbf = dbf; _dbInit = dbInit; _pdf = pdf; _excel = excel; _dialogs = dialogs;
    }

    partial void OnSelectedMonthChanged(MonthOption value) => _ = LoadAsync();
    partial void OnSelectedYearChanged(int value) => _ = LoadAsync();

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
        await _dbInit.ReadyTask;

        using var db = await _dbf.CreateDbContextAsync();

        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.Name)
            .ToListAsync();

        var ids = employees.Select(e => e.Id).ToList();

        var attendance = await db.AttendanceRecords.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId)
                     && a.Year == SelectedYear
                     && a.Month == SelectedMonth.Number)
            .ToDictionaryAsync(a => a.EmployeeId, a => a);

        var balances = await db.Advances.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId))
            .SumBalancesByEmployeeAsync();

        var prevDeductions = Rows.ToDictionary(r => r.EmployeeId, r => r.AdvanceDeductionEntry);

        var newRows = new List<SalaryRowVm>(employees.Count);
        int serial = 0;
        foreach (var e in employees)
        {
            var rec = attendance.GetValueOrDefault(e.Id);
            // Pro-rata: if employee joined this month and no record saved yet,
            // pre-fill the days before joining as absent
            int proRata = 0;
            if (e.JoiningDate.HasValue && rec is null)
            {
                var jd = e.JoiningDate.Value;
                if (jd.Year == SelectedYear && jd.Month == SelectedMonth.Number && jd.Day > 1)
                    proRata = jd.Day - 1;
            }

            var row = new SalaryRowVm
            {
                SerialNumber   = ++serial,
                EmployeeId     = e.Id,
                Name           = e.Name,
                BaseSalary     = e.BaseSalary,
                Year           = SelectedYear,
                Month          = SelectedMonth.Number,
                AdvanceBalance = balances.GetValueOrDefault(e.Id, 0m),
                AccountNumber  = e.AccountNumber,
                IfscCode       = e.IfscCode,
                PaymentMode    = e.PaymentMode,
                ProRataDays    = proRata,
            };

            // Set editable fields without triggering partial recalc individually
            row.DaysAbsent      = (rec?.DaysAbsent ?? 0) + proRata;
            row.EsicDeduction   = rec?.EsicDeduction ?? 0m;
            row.PfDeduction     = rec?.PfDeduction ?? 0m;
            row.AdvanceDeductionEntry = prevDeductions.GetValueOrDefault(e.Id, 0m);
            row.Recalculate();

            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SalaryRowVm.NetSalary))
                {
                    TotalNet       = Rows.Sum(r => r.NetSalary);
                    TotalDeduction = Rows.Sum(r => r.Deduction + Math.Max(0, r.AdvanceDeductionEntry));
                }
            };

            newRows.Add(row);
        }

        Rows.ReplaceAll(newRows);

        TotalNet       = Rows.Sum(r => r.NetSalary);
        TotalGross     = Rows.Sum(r => r.BaseSalary);
        TotalDeduction = Rows.Sum(r => r.Deduction + Math.Max(0, r.AdvanceDeductionEntry));
        EmployeeCount  = Rows.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAttendanceAsync()
    {
        try
        {
            using var db = await _dbf.CreateDbContextAsync();

            var empIds = Rows.Select(r => r.EmployeeId).ToList();
            var existing = await db.AttendanceRecords
                .Where(a => a.Year == SelectedYear
                         && a.Month == SelectedMonth.Number
                         && empIds.Contains(a.EmployeeId))
                .ToDictionaryAsync(a => a.EmployeeId);

            var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth.Number);

            foreach (var r in Rows)
            {
                if (existing.TryGetValue(r.EmployeeId, out var rec))
                {
                    rec.DaysAbsent    = Math.Clamp(r.DaysAbsent, 0, daysInMonth);
                    rec.EsicDeduction = Math.Max(0, r.EsicDeduction);
                    rec.PfDeduction   = Math.Max(0, r.PfDeduction);
                }
                else
                {
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        EmployeeId    = r.EmployeeId,
                        Year          = SelectedYear,
                        Month         = SelectedMonth.Number,
                        DaysAbsent    = Math.Clamp(r.DaysAbsent, 0, daysInMonth),
                        EsicDeduction = Math.Max(0, r.EsicDeduction),
                        PfDeduction   = Math.Max(0, r.PfDeduction),
                    });
                }
            }

            await db.SaveChangesAsync();
            _dialogs.Info("Attendance saved.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task PostDeductionsAsync()
    {
        var toPost = Rows.Where(r => r.AdvanceDeductionEntry > 0).ToList();
        if (toPost.Count == 0) { _dialogs.Error("No deduction amounts entered."); return; }

        var noteKey = $"SAL:{SelectedYear:0000}-{SelectedMonth.Number:00}";
        var postIds = toPost.Select(r => r.EmployeeId).ToList();

        try
        {
            using var db = await _dbf.CreateDbContextAsync();

            var existing = await db.Advances
                .Where(a => postIds.Contains(a.EmployeeId)
                         && a.Note == noteKey
                         && a.EntryType == AdvanceEntryType.Deducted)
                .ToListAsync();
            db.Advances.RemoveRange(existing);

            foreach (var row in toPost)
            {
                db.Advances.Add(new Advance
                {
                    EmployeeId = row.EmployeeId,
                    Date       = new DateTime(SelectedYear, SelectedMonth.Number, 1),
                    Amount     = row.AdvanceDeductionEntry,
                    EntryType  = AdvanceEntryType.Deducted,
                    Note       = noteKey
                });
            }

            await db.SaveChangesAsync();
            await LoadAsync();
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task GenerateSlipAsync(SalaryRowVm? row)
    {
        if (row is null) return;
        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var emp = await db.Employees.FindAsync(row.EmployeeId);
            if (emp is null) return;

            var year = SelectedYear;
            var month = SelectedMonth.Number;
            var daysAbsent = row.DaysAbsent;
            var esic = row.EsicDeduction;
            var pf = row.PfDeduction;
            var advBal = row.AdvanceBalance;
            var baseSalary = emp.BaseSalary;

            var path = await Task.Run(() =>
            {
                var b = SalaryCalculator.Compute(baseSalary, year, month, daysAbsent, esic, pf);
                var data = new SalarySlipData(emp, year, month, b, advBal);
                return _pdf.GenerateSlip(data);
            });
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        try
        {
            var defaultName = $"Payroll-{SelectedYear:0000}-{SelectedMonth.Number:00}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", defaultName);
            if (path is null) return;

            var data = Rows.Select(r => new MonthlySummaryRow(r.Name, r.BaseSalary, r.DaysAbsent,
                r.Deduction, r.EsicDeduction, r.PfDeduction, r.NetSalary)).ToList();
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _excel.ExportMonthlySummary(year, month, data, path));
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ExportSummaryPdfAsync()
    {
        try
        {
            var defaultName = $"Payroll-{SelectedYear:0000}-{SelectedMonth.Number:00}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", defaultName);
            if (path is null) return;

            var data = Rows.Select(r => new MonthlySummaryRow(r.Name, r.BaseSalary, r.DaysAbsent,
                r.Deduction, r.EsicDeduction, r.PfDeduction, r.NetSalary)).ToList();
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _pdf.GenerateMonthlySummary(year, month, data, path));
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ExportCashPdfAsync()
    {
        try
        {
            var cashRows = Rows.Where(r => r.PaymentMode == PaymentMode.Cash).ToList();
            if (cashRows.Count == 0) { _dialogs.Info("No cash-payment employees in this period."); return; }

            var defaultName = $"Cash-Salary-{SelectedYear:0000}-{SelectedMonth.Number:00}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", defaultName);
            if (path is null) return;

            var data = cashRows.Select(r => new MonthlySummaryRow(r.Name, r.BaseSalary, r.DaysAbsent,
                r.Deduction, r.EsicDeduction, r.PfDeduction, r.NetSalary)).ToList();
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _pdf.GenerateMonthlySummary(year, month, data, path));
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ExportIciciExcelAsync()
    {
        try
        {
            var defaultName = $"ICICI-Salary-{SelectedYear:0000}-{SelectedMonth.Number:00}.xlsx";
            var path = _dialogs.AskSavePath("Excel Workbook (*.xlsx)|*.xlsx", defaultName);
            if (path is null) return;

            var data = Rows
                .Where(r => r.PaymentMode != PaymentMode.Cash
                         && !string.IsNullOrWhiteSpace(r.AccountNumber)
                         && !string.IsNullOrWhiteSpace(r.IfscCode))
                .Select(r => new IciciPaymentRow(r.Name, r.AccountNumber, r.IfscCode, r.NetSalary, r.PaymentMode))
                .ToList();

            int skipped = Rows.Count(r => r.PaymentMode != PaymentMode.Cash) - data.Count;
            await Task.Run(() => _excel.ExportIciciPayment(data, path));
            OpenFile(path);

            if (skipped > 0)
                _dialogs.Info($"Exported {data.Count} employees. {skipped} skipped — bank details missing.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }
}
