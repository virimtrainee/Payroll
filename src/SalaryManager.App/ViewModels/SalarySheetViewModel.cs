using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using SalaryManager.Data.Validation;

namespace SalaryManager.App.ViewModels;

public partial class SalaryRowVm : ObservableObject
{
    public int SerialNumber { get; init; }
    public int EmployeeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string GroupDisplayName { get; init; } = string.Empty;
    public int ProRataDays { get; init; }
    public decimal BaseSalary { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal AdvanceBalance { get; init; }
    public decimal ExistingSalaryAdvanceDeduction { get; init; }
    public string? AccountNumber { get; init; }
    public string? IfscCode { get; init; }
    public PaymentMode PaymentMode { get; init; }
    public bool IsCashDeductionsDisabled { get; init; }
    public bool UsesTds => !IsCashDeductionsDisabled && BaseSalary > 25000m;
    public bool UsesEsicPf => !IsCashDeductionsDisabled && !UsesTds;

    // Editable attendance fields — trigger recalculation on change
    [ObservableProperty] private int daysAbsent;
    [ObservableProperty] private decimal esicDeduction;
    [ObservableProperty] private decimal pfDeduction;
    [ObservableProperty] private decimal tdsDeduction;
    [ObservableProperty] private decimal advanceDeductionEntry;
    [ObservableProperty] private bool isNetSalaryOverrideEnabled;
    [ObservableProperty] private decimal netSalaryOverride;

    // Computed display fields — updated by Recalculate()
    [ObservableProperty] private decimal perDay;
    [ObservableProperty] private decimal deduction;
    [ObservableProperty] private decimal calculatedNetSalary;
    [ObservableProperty] private decimal netSalary;

    private bool _isRecalculating;

    partial void OnDaysAbsentChanged(int value) => Recalculate();
    partial void OnEsicDeductionChanged(decimal value) => Recalculate();
    partial void OnPfDeductionChanged(decimal value) => Recalculate();
    partial void OnTdsDeductionChanged(decimal value) => Recalculate();
    partial void OnAdvanceDeductionEntryChanged(decimal value) => Recalculate();
    partial void OnIsNetSalaryOverrideEnabledChanged(bool value) => Recalculate();
    partial void OnNetSalaryOverrideChanged(decimal value)
    {
        if (!_isRecalculating)
            Recalculate();
    }

    public void Recalculate()
    {
        _isRecalculating = true;
        try
        {
            var b = SalaryCalculator.Compute(BaseSalary, Year, Month,
                Math.Max(0, DaysAbsent),
                UsesEsicPf ? Math.Max(0, EsicDeduction) : 0m,
                UsesEsicPf ? Math.Max(0, PfDeduction) : 0m,
                UsesTds ? Math.Max(0, TdsDeduction) : 0m);
            PerDay = b.PerDayRate;
            Deduction = b.Deduction;
            CalculatedNetSalary = b.NetSalary - Math.Max(0, AdvanceDeductionEntry);
            if (!IsNetSalaryOverrideEnabled)
            {
                NetSalaryOverride = CalculatedNetSalary;
            }
            NetSalary = IsNetSalaryOverrideEnabled
                ? Math.Max(0, NetSalaryOverride)
                : CalculatedNetSalary;
        }
        finally
        {
            _isRecalculating = false;
        }
    }
}

public partial class SalarySheetViewModel : ObservableObject
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
    [ObservableProperty] private decimal totalNet;
    [ObservableProperty] private decimal totalGross;
    [ObservableProperty] private decimal totalDeduction;
    [ObservableProperty] private int totalDaysAbsent;
    [ObservableProperty] private decimal totalEsic;
    [ObservableProperty] private decimal totalPf;
    [ObservableProperty] private decimal totalTds;
    [ObservableProperty] private decimal totalAdvanceDeduction;
    [ObservableProperty] private decimal totalAbsenceDeduction;
    [ObservableProperty] private decimal totalAdvanceBalance;
    [ObservableProperty] private int employeeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridReadOnly))]
    [NotifyPropertyChangedFor(nameof(EditButtonText))]
    private bool isEditMode;

    [ObservableProperty] private bool isLoading;

    public bool IsGridReadOnly => !IsEditMode;
    public string EditButtonText => IsEditMode ? "Done" : "Edit";

    public RangeObservableCollection<SalaryRowVm> Rows { get; } = new();
    public ICollectionView RowsView { get; }
    public RangeObservableCollection<SelectableGroupFilterOptionVm> GroupFilterOptions { get; } = new();

    private bool _updatingGroupFilter;
    private int _loadVersion;
    private static readonly string CashGroupNormalizedName = GroupNameNormalizer.Normalize("Cash");

    public SalarySheetViewModel(IDbContextFactory<AppDbContext> dbf,
                                DatabaseInitializer dbInit,
                                PdfSlipService pdf,
                                ExcelExportService excel,
                                DialogService dialogs,
                                AppSettingsService settings)
    {
        _dbf = dbf; _dbInit = dbInit; _pdf = pdf; _excel = excel; _dialogs = dialogs; _settings = settings;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
    }

    partial void OnSelectedMonthChanged(MonthOption value) => LoadCommand.Execute(null);
    partial void OnSelectedYearChanged(int value) => LoadCommand.Execute(null);

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        try
        {
            await _dbInit.ReadyTask;

            using var db = await _dbf.CreateDbContextAsync();
            await LoadGroupFiltersAsync(db);

            var selectedGroups = GroupFilterOptions
                .Where(g => g.IsSelected)
                .Select(g => new SalaryGroupSortOption(g.Id, g.Name))
                .OrderBy(g => g.Name)
                .ToList();
            var selectedGroupIds = selectedGroups.Select(g => g.Id).ToList();

            var employeeQuery = db.Employees.AsNoTracking()
                .Include(e => e.GroupMemberships)
                    .ThenInclude(m => m.EmployeeGroup)
                .Where(e => e.IsActive);
            if (selectedGroupIds.Count > 0)
            {
                employeeQuery = employeeQuery.Where(e =>
                    e.GroupMemberships.Any(m => selectedGroupIds.Contains(m.EmployeeGroupId)));
            }

            var employees = await employeeQuery.OrderBy(e => e.Name).ToListAsync();
            var rowGroups = employees.ToDictionary(
                e => e.Id,
                e => selectedGroups.Count == 0
                    ? string.Empty
                    : ResolveSelectedGroupName(e, selectedGroups));

            employees = selectedGroups.Count == 0
                ? employees.OrderBy(e => e.Name).ToList()
                : employees
                    .OrderBy(e => rowGroups[e.Id])
                    .ThenBy(e => e.Name)
                    .ToList();

            var ids = employees.Select(e => e.Id).ToList();
            var sourceKey = AdvanceSourceKeys.Salary(SelectedYear, SelectedMonth.Number);

            var attendance = await db.AttendanceRecords.AsNoTracking()
                .Where(a => ids.Contains(a.EmployeeId)
                         && a.Year == SelectedYear
                         && a.Month == SelectedMonth.Number)
                .ToDictionaryAsync(a => a.EmployeeId, a => a);

            var balances = await db.Advances.AsNoTracking()
                .Where(a => ids.Contains(a.EmployeeId))
                .SumBalancesByEmployeeAsync();

            var salaryAdvanceDeductions = await db.Advances.AsNoTracking()
                .Where(a => ids.Contains(a.EmployeeId)
                         && a.SourceKey == sourceKey
                         && a.EntryType == AdvanceEntryType.Deducted)
                .ToDictionaryAsync(a => a.EmployeeId, a => a.Amount);

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
                    SerialNumber = ++serial,
                    EmployeeId = e.Id,
                    Name = e.Name,
                    GroupDisplayName = rowGroups[e.Id],
                    BaseSalary = e.BaseSalary,
                    Year = SelectedYear,
                    Month = SelectedMonth.Number,
                    AdvanceBalance = balances.GetValueOrDefault(e.Id, 0m),
                    ExistingSalaryAdvanceDeduction = salaryAdvanceDeductions.GetValueOrDefault(e.Id, 0m),
                    AccountNumber = e.AccountNumber,
                    IfscCode = e.IfscCode,
                    PaymentMode = e.PaymentMode,
                    IsCashDeductionsDisabled = HasCashDeductionsDisabled(e),
                    ProRataDays = proRata,
                };

                // Set editable fields without triggering partial recalc individually
                row.DaysAbsent = (rec?.DaysAbsent ?? 0) + proRata;
                row.EsicDeduction = row.UsesEsicPf ? rec?.EsicDeduction ?? 0m : 0m;
                row.PfDeduction = row.UsesEsicPf ? rec?.PfDeduction ?? 0m : 0m;
                row.TdsDeduction = row.UsesTds ? rec?.TdsDeduction ?? 0m : 0m;
                row.AdvanceDeductionEntry = row.ExistingSalaryAdvanceDeduction;
                row.Recalculate();
                if (rec?.NetSalaryOverride is decimal netSalaryOverride)
                {
                    row.IsNetSalaryOverrideEnabled = true;
                    row.NetSalaryOverride = Math.Max(0, netSalaryOverride);
                    row.Recalculate();
                }

                row.PropertyChanged += (_, _) => RefreshTotals();

                newRows.Add(row);
            }

            if (version != _loadVersion) return;
            Rows.ReplaceAll(newRows);
            ApplyRowGrouping(selectedGroups.Count > 0);
            RefreshTotals();
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

    [RelayCommand]
    private async Task SaveAttendanceAsync()
    {
        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var validation = ValidateRows(Rows, includeAdvance: false);
            if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

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
                    rec.DaysAbsent = r.DaysAbsent;
                    rec.EsicDeduction = EffectiveEsicDeduction(r);
                    rec.PfDeduction = EffectivePfDeduction(r);
                    rec.TdsDeduction = EffectiveTdsDeduction(r);
                    rec.NetSalaryOverride = EffectiveNetSalaryOverride(r);
                }
                else
                {
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        EmployeeId = r.EmployeeId,
                        Year = SelectedYear,
                        Month = SelectedMonth.Number,
                        DaysAbsent = r.DaysAbsent,
                        EsicDeduction = EffectiveEsicDeduction(r),
                        PfDeduction = EffectivePfDeduction(r),
                        TdsDeduction = EffectiveTdsDeduction(r),
                        NetSalaryOverride = EffectiveNetSalaryOverride(r),
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
        var rows = Rows.ToList();
        if (rows.Count == 0) { _dialogs.Error("No salary rows loaded."); return; }
        var validation = ValidateRows(rows, includeAdvance: true);
        if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

        var sourceKey = AdvanceSourceKeys.Salary(SelectedYear, SelectedMonth.Number);
        var postIds = rows.Select(r => r.EmployeeId).ToList();

        try
        {
            using var db = await _dbf.CreateDbContextAsync();

            var existing = await db.Advances
                .Where(a => postIds.Contains(a.EmployeeId)
                         && a.SourceKey == sourceKey)
                .ToDictionaryAsync(a => a.EmployeeId);

            foreach (var row in rows)
            {
                var amount = row.AdvanceDeductionEntry;
                if (amount <= 0)
                {
                    if (existing.TryGetValue(row.EmployeeId, out var remove))
                        db.Advances.Remove(remove);
                    continue;
                }

                if (existing.TryGetValue(row.EmployeeId, out var advance))
                {
                    advance.Date = new DateTime(SelectedYear, SelectedMonth.Number, 1);
                    advance.Amount = amount;
                    advance.EntryType = AdvanceEntryType.Deducted;
                }
                else
                {
                    db.Advances.Add(new Advance
                    {
                        EmployeeId = row.EmployeeId,
                        Date = new DateTime(SelectedYear, SelectedMonth.Number, 1),
                        Amount = amount,
                        EntryType = AdvanceEntryType.Deducted,
                        Note = sourceKey,
                        SourceKey = sourceKey
                    });
                }
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
            var validation = ValidateRows([row], includeAdvance: true);
            if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

            var year = SelectedYear;
            var month = SelectedMonth.Number;
            var daysAbsent = row.DaysAbsent;
            var esic = EffectiveEsicDeduction(row);
            var pf = EffectivePfDeduction(row);
            var tds = EffectiveTdsDeduction(row);
            var advBal = row.AdvanceBalance;
            var advDeduction = row.AdvanceDeductionEntry;
            decimal? netSalaryOverride = row.IsNetSalaryOverrideEnabled ? row.NetSalary : null;
            var baseSalary = emp.BaseSalary;

            var path = await Task.Run(() =>
            {
                var b = SalaryCalculator.Compute(baseSalary, year, month, daysAbsent, esic, pf, tds);
                var data = new SalarySlipData(emp, year, month, b, advBal, advDeduction, netSalaryOverride);
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

            var validation = ValidateRows(Rows, includeAdvance: true);
            if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

            var data = BuildMonthlySummaryRows(Rows);
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

            var validation = ValidateRows(Rows, includeAdvance: true);
            if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

            var data = BuildMonthlySummaryRows(Rows);
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
            var validation = ValidateRows(cashRows, includeAdvance: true);
            if (!validation.IsValid) { _dialogs.Error(validation.ToMessage()); return; }

            var defaultName = $"Cash-Salary-{SelectedYear:0000}-{SelectedMonth.Number:00}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", defaultName);
            if (path is null) return;

            var data = BuildMonthlySummaryRows(cashRows);
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

            var payableRows = Rows.Where(r => r.PaymentMode != PaymentMode.Cash).ToList();
            if (payableRows.Count == 0) { _dialogs.Info("No bank-payment employees in this period."); return; }

            var validation = ValidateRows(payableRows, includeAdvance: true);
            var exportIssues = ValidateIciciRows(payableRows);
            if (!validation.IsValid || exportIssues.Count > 0)
            {
                var issues = validation.Issues.Concat(exportIssues).ToList();
                _dialogs.Error(new ValidationResult(issues).ToMessage());
                return;
            }

            var settings = _settings.Load();
            var options = _dialogs.AskIciciExportOptions(settings.IciciDebitAccountNo);
            if (options is null) return;
            _settings.Save(settings with { IciciDebitAccountNo = options.DebitAccountNo });

            var data = payableRows
                .Select(r => new IciciPaymentRow(r.Name, r.AccountNumber, r.IfscCode, r.NetSalary, r.PaymentMode))
                .ToList();

            await Task.Run(() => _excel.ExportIciciPayment(data, options, path));
            OpenFile(path);
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private async Task LoadGroupFiltersAsync(AppDbContext db)
    {
        var selectedIds = GroupFilterOptions
            .Where(g => g.IsSelected)
            .Select(g => g.Id)
            .ToHashSet();

        var groups = await db.EmployeeGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new SelectableGroupFilterOptionVm
            {
                Id = g.Id,
                Name = g.Name,
                IsSelected = selectedIds.Contains(g.Id)
            })
            .ToListAsync();

        _updatingGroupFilter = true;
        try
        {
            foreach (var option in GroupFilterOptions)
                option.PropertyChanged -= OnGroupFilterOptionPropertyChanged;

            GroupFilterOptions.ReplaceAll(groups);

            foreach (var option in GroupFilterOptions)
                option.PropertyChanged += OnGroupFilterOptionPropertyChanged;
        }
        finally
        {
            _updatingGroupFilter = false;
        }
    }

    private void OnGroupFilterOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingGroupFilter || e.PropertyName != nameof(SelectableGroupFilterOptionVm.IsSelected)) return;
        LoadCommand.Execute(null);
    }

    private void ApplyRowGrouping(bool isGrouped)
    {
        RowsView.GroupDescriptions.Clear();
        if (isGrouped)
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SalaryRowVm.GroupDisplayName)));
        RowsView.Refresh();
    }

    private void RefreshTotals()
    {
        TotalNet = Rows.Sum(r => r.NetSalary);
        TotalGross = Rows.Sum(r => r.BaseSalary);
        TotalDaysAbsent = Rows.Sum(r => r.DaysAbsent);
        TotalEsic = Rows.Sum(EffectiveEsicDeduction);
        TotalPf = Rows.Sum(EffectivePfDeduction);
        TotalTds = Rows.Sum(EffectiveTdsDeduction);
        TotalAdvanceDeduction = Rows.Sum(r => Math.Max(0, r.AdvanceDeductionEntry));
        TotalAbsenceDeduction = Rows.Sum(r => r.Deduction);
        TotalAdvanceBalance = Rows.Sum(r => r.AdvanceBalance);
        TotalDeduction = TotalGross - TotalNet;
        EmployeeCount = Rows.Count;
    }

    private static List<MonthlySummaryRow> BuildMonthlySummaryRows(IEnumerable<SalaryRowVm> rows)
        => rows.Select(r =>
        {
            var b = SalaryCalculator.Compute(r.BaseSalary, r.Year, r.Month, r.DaysAbsent,
                EffectiveEsicDeduction(r), EffectivePfDeduction(r), EffectiveTdsDeduction(r));
            return new MonthlySummaryRow(r.Name, r.BaseSalary, r.DaysAbsent, b.Deduction,
                b.EsicDeduction, b.PfDeduction, b.TdsDeduction,
                Math.Max(0, r.AdvanceDeductionEntry), r.NetSalary);
        }).ToList();

    private static ValidationResult ValidateRows(IEnumerable<SalaryRowVm> rows, bool includeAdvance)
    {
        var issues = rows
            .SelectMany(row => PayrollValidator.Validate(ToPayrollInput(row, includeAdvance)).Issues)
            .ToList();
        issues.AddRange(rows
            .Where(row => row.IsNetSalaryOverrideEnabled && row.NetSalaryOverride < 0)
            .Select(row => new ValidationIssue(row.Name, "Net salary override cannot be negative.", "net_override_negative")));
        return new ValidationResult(issues);
    }

    private static PayrollValidationInput ToPayrollInput(SalaryRowVm row, bool includeAdvance)
        => new(
            row.Name,
            row.BaseSalary,
            row.Year,
            row.Month,
            row.DaysAbsent,
            EffectiveEsicDeduction(row),
            EffectivePfDeduction(row),
            EffectiveTdsDeduction(row),
            includeAdvance ? row.AdvanceDeductionEntry : 0m,
            row.AdvanceBalance,
            row.ExistingSalaryAdvanceDeduction);

    private static decimal EffectiveEsicDeduction(SalaryRowVm row)
        => row.UsesEsicPf ? Math.Max(0, row.EsicDeduction) : 0m;

    private static decimal EffectivePfDeduction(SalaryRowVm row)
        => row.UsesEsicPf ? Math.Max(0, row.PfDeduction) : 0m;

    private static decimal EffectiveTdsDeduction(SalaryRowVm row)
        => row.UsesTds ? Math.Max(0, row.TdsDeduction) : 0m;

    private static decimal? EffectiveNetSalaryOverride(SalaryRowVm row)
        => row.IsNetSalaryOverrideEnabled ? Math.Max(0, row.NetSalaryOverride) : null;

    private static bool HasCashDeductionsDisabled(Employee employee)
        => employee.PaymentMode == PaymentMode.Cash
        || employee.GroupMemberships.Any(m =>
            m.EmployeeGroup is not null
            && GroupNameNormalizer.Normalize(m.EmployeeGroup.Name) == CashGroupNormalizedName);

    private static List<ValidationIssue> ValidateIciciRows(IEnumerable<SalaryRowVm> rows)
    {
        var issues = new List<ValidationIssue>();
        foreach (var row in rows)
        {
            var employeeValidation = EmployeeValidator.Validate(new EmployeeValidationInput(
                row.Name,
                row.BaseSalary,
                row.AccountNumber,
                row.IfscCode,
                row.PaymentMode,
                null));
            issues.AddRange(employeeValidation.Issues.Select(i => i with { Field = $"{row.Name} - {i.Field}" }));

            if (row.NetSalary <= 0)
                issues.Add(new(row.Name, "ICICI export amount must be greater than zero."));
        }

        return issues;
    }

    private sealed record SalaryGroupSortOption(int Id, string Name);

    private static string ResolveSelectedGroupName(Employee employee, IReadOnlyList<SalaryGroupSortOption> selectedGroups)
    {
        var membershipIds = employee.GroupMemberships.Select(m => m.EmployeeGroupId).ToHashSet();
        return selectedGroups.FirstOrDefault(g => membershipIds.Contains(g.Id))?.Name ?? "Ungrouped";
    }
}
