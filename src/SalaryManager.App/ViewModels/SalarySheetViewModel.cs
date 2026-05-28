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
    public decimal EmployeeBaseSalary { get; init; }
    public decimal BaseSalary => EffectiveBaseSalary;
    public decimal EffectiveBaseSalary
        => IsBaseSalaryOverrideEnabled && BaseSalaryOverride is decimal overrideSalary && overrideSalary >= 0m
            ? overrideSalary
            : EmployeeBaseSalary;
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal AdvanceBalance { get; init; }
    public decimal ExistingSalaryAdvanceDeduction { get; init; }
    public decimal? HistoricalNetSalaryOverride { get; init; }
    public string? AccountNumber { get; init; }
    public string? IfscCode { get; init; }
    public PaymentMode PaymentMode { get; init; }
    public string PaymentModeLabel => PaymentMode switch
    {
        PaymentMode.Cash => "Cash",
        PaymentMode.IciciBank => "ICICI",
        _ => "Other"
    };
    public bool IsCashDeductionsDisabled { get; init; }
    public bool UsesTds => !IsCashDeductionsDisabled && EffectiveBaseSalary > 25000m;
    public bool UsesEsicPf => !IsCashDeductionsDisabled && !UsesTds;
    public decimal TotalDeductions => Math.Max(0, EffectiveBaseSalary - NetSalary);
    public bool HasInvalidBaseSalaryOverride => IsBaseSalaryOverrideEnabled
        && (!BaseSalaryOverride.HasValue || BaseSalaryOverride.Value < 0m);
    public string BaseSalaryOverrideValidationMessage
        => !IsBaseSalaryOverrideEnabled
            ? string.Empty
            : !BaseSalaryOverride.HasValue
                ? "Override base salary is required."
                : BaseSalaryOverride.Value < 0m
                    ? "Override base salary cannot be negative."
                    : string.Empty;
    private int _recalculationSuppression;
    private bool IsRecalculationSuppressed => _recalculationSuppression > 0;

    // Editable attendance fields — trigger recalculation on change
    [ObservableProperty] private int daysAbsent;
    [ObservableProperty] private decimal esicDeduction;
    [ObservableProperty] private decimal pfDeduction;
    [ObservableProperty] private decimal tdsDeduction;
    [ObservableProperty] private decimal advanceDeductionEntry;
    [ObservableProperty] private bool isBaseSalaryOverrideEnabled;
    [ObservableProperty] private decimal? baseSalaryOverride;

    // Computed display fields — updated by Recalculate()
    [ObservableProperty] private decimal perDay;
    [ObservableProperty] private decimal deduction;
    [ObservableProperty] private decimal calculatedNetSalary;
    [ObservableProperty] private decimal netSalary;

    public void InitializeEditableValues(
        bool isBaseSalaryOverrideEnabled,
        decimal? baseSalaryOverride,
        int daysAbsent,
        decimal esicDeduction,
        decimal pfDeduction,
        decimal tdsDeduction,
        decimal advanceDeductionEntry)
    {
        _recalculationSuppression++;
        try
        {
            BaseSalaryOverride = isBaseSalaryOverrideEnabled ? baseSalaryOverride : null;
            IsBaseSalaryOverrideEnabled = isBaseSalaryOverrideEnabled;
            DaysAbsent = daysAbsent;
            EsicDeduction = esicDeduction;
            PfDeduction = pfDeduction;
            TdsDeduction = tdsDeduction;
            AdvanceDeductionEntry = advanceDeductionEntry;
        }
        finally
        {
            _recalculationSuppression--;
        }

        RefreshSalaryBasisAndRecalculate();
    }

    partial void OnDaysAbsentChanged(int value) => RecalculateIfNotSuppressed();
    partial void OnEsicDeductionChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnPfDeductionChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnTdsDeductionChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnAdvanceDeductionEntryChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnIsBaseSalaryOverrideEnabledChanged(bool value)
    {
        if (!IsRecalculationSuppressed)
        {
            if (value && !BaseSalaryOverride.HasValue)
                BaseSalaryOverride = EmployeeBaseSalary;
            if (!value)
                BaseSalaryOverride = null;
        }

        OnSalaryBasisChanged();
    }

    partial void OnBaseSalaryOverrideChanged(decimal? value) => OnSalaryBasisChanged();

    private void OnSalaryBasisChanged()
    {
        if (IsRecalculationSuppressed)
            return;

        RefreshSalaryBasisAndRecalculate();
    }

    private void RefreshSalaryBasisAndRecalculate()
    {
        if (!HasInvalidBaseSalaryOverride)
        {
            _recalculationSuppression++;
            try
            {
                ClearInapplicableDeductions();
            }
            finally
            {
                _recalculationSuppression--;
            }
        }

        OnPropertyChanged(nameof(EffectiveBaseSalary));
        OnPropertyChanged(nameof(BaseSalary));
        OnPropertyChanged(nameof(UsesTds));
        OnPropertyChanged(nameof(UsesEsicPf));
        OnPropertyChanged(nameof(TotalDeductions));
        OnPropertyChanged(nameof(HasInvalidBaseSalaryOverride));
        OnPropertyChanged(nameof(BaseSalaryOverrideValidationMessage));
        Recalculate();
    }

    private void RecalculateIfNotSuppressed()
    {
        if (!IsRecalculationSuppressed)
            Recalculate();
    }

    private void ClearInapplicableDeductions()
    {
        if (UsesTds)
        {
            if (EsicDeduction != 0m) EsicDeduction = 0m;
            if (PfDeduction != 0m) PfDeduction = 0m;
        }
        else
        {
            if (TdsDeduction != 0m) TdsDeduction = 0m;
        }
    }

    public void Recalculate()
    {
        if (HasInvalidBaseSalaryOverride)
        {
            OnPropertyChanged(nameof(TotalDeductions));
            return;
        }

        var b = SalaryCalculator.Compute(EffectiveBaseSalary, Year, Month,
            Math.Max(0, DaysAbsent),
            UsesEsicPf ? Math.Max(0, EsicDeduction) : 0m,
            UsesEsicPf ? Math.Max(0, PfDeduction) : 0m,
            UsesTds ? Math.Max(0, TdsDeduction) : 0m);
        PerDay = b.PerDayRate;
        Deduction = b.Deduction;
        CalculatedNetSalary = b.NetSalary - Math.Max(0, AdvanceDeductionEntry);
        NetSalary = !IsBaseSalaryOverrideEnabled && HistoricalNetSalaryOverride is decimal netOverride
            ? Math.Max(0, netOverride)
            : CalculatedNetSalary;
        OnPropertyChanged(nameof(EffectiveBaseSalary));
        OnPropertyChanged(nameof(BaseSalary));
        OnPropertyChanged(nameof(TotalDeductions));
    }
}

public partial class SalarySheetViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly DatabaseInitializer _dbInit;
    private readonly MonthlyPayrollService _payroll;
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
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private GroupFilterOptionVm? selectedGroupDropdownFilter;

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
    public RangeObservableCollection<GroupFilterOptionVm> GroupDropdownOptions { get; } = new();
    public bool IsAllGroupsSelected => GroupFilterOptions.All(g => !g.IsSelected);

    private bool _updatingGroupFilter;
    private int _loadVersion;
    private static readonly HashSet<string> TotalRefreshInputProperties = new(StringComparer.Ordinal)
    {
        nameof(SalaryRowVm.DaysAbsent),
        nameof(SalaryRowVm.EsicDeduction),
        nameof(SalaryRowVm.PfDeduction),
        nameof(SalaryRowVm.TdsDeduction),
        nameof(SalaryRowVm.AdvanceDeductionEntry),
        nameof(SalaryRowVm.IsBaseSalaryOverrideEnabled),
        nameof(SalaryRowVm.BaseSalaryOverride)
    };

    public SalarySheetViewModel(IDbContextFactory<AppDbContext> dbf,
                                DatabaseInitializer dbInit,
                                MonthlyPayrollService payroll,
                                PdfSlipService pdf,
                                ExcelExportService excel,
                                DialogService dialogs,
                                AppSettingsService settings)
    {
        _dbf = dbf; _dbInit = dbInit; _payroll = payroll; _pdf = pdf; _excel = excel; _dialogs = dialogs; _settings = settings;
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = RowFilter;
    }

    partial void OnSelectedMonthChanged(MonthOption value) => LoadCommand.Execute(null);
    partial void OnSelectedYearChanged(int value) => LoadCommand.Execute(null);
    partial void OnSearchTextChanged(string value) => RowsView.Refresh();
    partial void OnSelectedGroupDropdownFilterChanged(GroupFilterOptionVm? value)
    {
        if (_updatingGroupFilter || value is null) return;

        _updatingGroupFilter = true;
        try
        {
            foreach (var option in GroupFilterOptions)
                option.IsSelected = value.Id is int id && option.Id == id;
        }
        finally
        {
            _updatingGroupFilter = false;
        }

        OnPropertyChanged(nameof(IsAllGroupsSelected));
        LoadCommand.Execute(null);
    }

    private bool RowFilter(object item)
    {
        if (item is not SalaryRowVm row) return false;
        var search = SearchText.Trim();
        return string.IsNullOrWhiteSpace(search)
            || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    public IReadOnlyDictionary<string, double> LoadColumnWidths()
        => _settings.Load().SalarySheetColumnWidths ?? new Dictionary<string, double>();

    public void SaveColumnWidths(IReadOnlyDictionary<string, double> widths)
    {
        var clean = widths
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key)
                          && double.IsFinite(kvp.Value)
                          && kvp.Value >= 40)
            .ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value, 2));

        var settings = _settings.Load();
        _settings.Save(settings with { SalarySheetColumnWidths = clean });
    }

    [RelayCommand]
    private void ClearGroupFilters()
    {
        _updatingGroupFilter = true;
        try
        {
            foreach (var option in GroupFilterOptions)
                option.IsSelected = false;
            SelectedGroupDropdownFilter = GroupDropdownOptions.FirstOrDefault(o => o.Id is null);
        }
        finally
        {
            _updatingGroupFilter = false;
        }

        OnPropertyChanged(nameof(IsAllGroupsSelected));
        LoadCommand.Execute(null);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var year = SelectedYear;
        var month = SelectedMonth.Number;
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

            var snapshot = await _payroll.LoadAsync(new MonthlyPayrollRequest(year, month, selectedGroupIds));
            if (version != _loadVersion) return;

            var attendanceStates = await LoadAttendanceStatesAsync(
                db,
                snapshot.Rows.Select(r => r.EmployeeId).ToList(),
                year,
                month);
            if (version != _loadVersion) return;

            var rowGroups = snapshot.Rows.ToDictionary(
                r => r.EmployeeId,
                r => selectedGroups.Count == 0
                    ? r.PrimaryGroupName
                    : ResolveSelectedGroupName(r, selectedGroups));

            var payrollRows = selectedGroups.Count == 0
                ? snapshot.Rows.OrderBy(r => r.Name).ToList()
                : snapshot.Rows
                    .OrderBy(r => rowGroups[r.EmployeeId])
                    .ThenBy(r => r.Name)
                    .ToList();

            var newRows = new List<SalaryRowVm>(payrollRows.Count);
            int serial = 0;
            foreach (var payrollRow in payrollRows)
            {
                attendanceStates.TryGetValue(payrollRow.EmployeeId, out var attendanceState);
                int proRata = 0;
                if (payrollRow.JoiningDate.HasValue && attendanceState is null)
                {
                    var jd = payrollRow.JoiningDate.Value;
                    if (jd.Year == year && jd.Month == month && jd.Day > 1)
                        proRata = jd.Day - 1;
                }

                var row = new SalaryRowVm
                {
                    SerialNumber = ++serial,
                    EmployeeId = payrollRow.EmployeeId,
                    Name = payrollRow.Name,
                    GroupDisplayName = rowGroups[payrollRow.EmployeeId],
                    EmployeeBaseSalary = payrollRow.EmployeeBaseSalary,
                    Year = year,
                    Month = month,
                    AdvanceBalance = payrollRow.AdvanceBalance,
                    ExistingSalaryAdvanceDeduction = payrollRow.AdvanceDeduction,
                    HistoricalNetSalaryOverride = payrollRow.HistoricalNetSalaryOverride,
                    AccountNumber = payrollRow.AccountNumber,
                    IfscCode = payrollRow.IfscCode,
                    PaymentMode = payrollRow.PaymentMode,
                    IsCashDeductionsDisabled = payrollRow.IsCashDeductionsDisabled,
                    ProRataDays = proRata,
                };

                row.InitializeEditableValues(
                    attendanceState?.BaseSalaryOverride is not null,
                    attendanceState?.BaseSalaryOverride,
                    payrollRow.DaysAbsent + proRata,
                    payrollRow.EsicDeduction,
                    payrollRow.PfDeduction,
                    payrollRow.TdsDeduction,
                    payrollRow.AdvanceDeduction);

                newRows.Add(row);
            }

            if (version != _loadVersion) return;
            foreach (var row in Rows)
                row.PropertyChanged -= OnSalaryRowPropertyChanged;
            foreach (var row in newRows)
                row.PropertyChanged += OnSalaryRowPropertyChanged;
            Rows.ReplaceAll(newRows);
            ApplyRowGrouping(selectedGroups.Count > 0);
            RefreshTotals();
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

    [RelayCommand]
    private async Task SaveAttendanceAsync()
    {
        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var validation = ValidateRows(Rows, includeAdvance: false);
            if (!validation.IsValid) { await _dialogs.ErrorAsync(validation.ToMessage()); return; }

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
                    rec.BaseSalaryOverride = EffectiveBaseSalaryOverride(r);
                    rec.NetSalaryOverride = null;
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
                        BaseSalaryOverride = EffectiveBaseSalaryOverride(r),
                        NetSalaryOverride = null,
                    });
                }
            }

            await db.SaveChangesAsync();
            await _dialogs.InfoAsync("Attendance saved.");
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task PostDeductionsAsync()
    {
        var rows = Rows.ToList();
        if (rows.Count == 0) { await _dialogs.ErrorAsync("No salary rows loaded."); return; }
        var validation = ValidateRows(rows, includeAdvance: true);
        if (!validation.IsValid) { await _dialogs.ErrorAsync(validation.ToMessage()); return; }

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
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
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
            if (!validation.IsValid) { await _dialogs.ErrorAsync(validation.ToMessage()); return; }

            var data = BuildMonthlySummaryRows(Rows);
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _excel.ExportMonthlySummary(year, month, data, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
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
            if (!validation.IsValid) { await _dialogs.ErrorAsync(validation.ToMessage()); return; }

            var data = BuildMonthlySummaryRows(Rows);
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _pdf.GenerateMonthlySummary(year, month, data, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task ExportCashPdfAsync()
    {
        try
        {
            var cashRows = Rows.Where(r => r.PaymentMode == PaymentMode.Cash).ToList();
            if (cashRows.Count == 0) { await _dialogs.InfoAsync("No cash-payment employees in this period."); return; }
            var validation = ValidateRows(cashRows, includeAdvance: true);
            if (!validation.IsValid) { await _dialogs.ErrorAsync(validation.ToMessage()); return; }

            var defaultName = $"Cash-Salary-{SelectedYear:0000}-{SelectedMonth.Number:00}.pdf";
            var path = _dialogs.AskSavePath("PDF (*.pdf)|*.pdf", defaultName);
            if (path is null) return;

            var data = BuildMonthlySummaryRows(cashRows);
            var year = SelectedYear;
            var month = SelectedMonth.Number;
            await Task.Run(() => _pdf.GenerateMonthlySummary(year, month, data, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
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
            if (payableRows.Count == 0) { await _dialogs.InfoAsync("No bank-payment employees in this period."); return; }

            var validation = ValidateRows(payableRows, includeAdvance: true);
            var exportIssues = ValidateIciciRows(payableRows);
            if (!validation.IsValid || exportIssues.Count > 0)
            {
                var issues = validation.Issues.Concat(exportIssues).ToList();
                await _dialogs.ErrorAsync(new ValidationResult(issues).ToMessage());
                return;
            }

            var settings = _settings.Load();
            var options = await _dialogs.AskIciciExportOptionsAsync(settings.IciciDebitAccountNo);
            if (options is null) return;
            _settings.Save(settings with { IciciDebitAccountNo = options.DebitAccountNo });

            var data = payableRows
                .Select(r => new IciciPaymentRow(r.Name, r.AccountNumber, r.IfscCode, r.NetSalary, r.PaymentMode))
                .ToList();

            await Task.Run(() => _excel.ExportIciciPayment(data, options, path));
            OpenFile(path);
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    private static async Task<Dictionary<int, AttendanceLoadState>> LoadAttendanceStatesAsync(
        AppDbContext db,
        IReadOnlyCollection<int> employeeIds,
        int year,
        int month)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<int, AttendanceLoadState>();

        return await db.AttendanceRecords.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)
                     && a.Year == year
                     && a.Month == month)
            .Select(a => new AttendanceLoadState(a.EmployeeId, a.BaseSalaryOverride))
            .ToDictionaryAsync(a => a.EmployeeId);
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
        var dropdownOptions = new List<GroupFilterOptionVm> { new(null, "All Groups") };
        dropdownOptions.AddRange(groups.Select(g => new GroupFilterOptionVm(g.Id, g.Name)));

        _updatingGroupFilter = true;
        try
        {
            foreach (var option in GroupFilterOptions)
                option.PropertyChanged -= OnGroupFilterOptionPropertyChanged;

            GroupFilterOptions.ReplaceAll(groups);
            GroupDropdownOptions.ReplaceAll(dropdownOptions);

            foreach (var option in GroupFilterOptions)
                option.PropertyChanged += OnGroupFilterOptionPropertyChanged;

            SelectedGroupDropdownFilter = selectedIds.Count == 1
                ? GroupDropdownOptions.FirstOrDefault(g => g.Id == selectedIds.First()) ?? GroupDropdownOptions[0]
                : GroupDropdownOptions[0];
            OnPropertyChanged(nameof(IsAllGroupsSelected));
        }
        finally
        {
            _updatingGroupFilter = false;
        }
    }

    private void OnGroupFilterOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingGroupFilter || e.PropertyName != nameof(SelectableGroupFilterOptionVm.IsSelected)) return;
        _updatingGroupFilter = true;
        try
        {
            var selected = GroupFilterOptions.Where(g => g.IsSelected).ToList();
            SelectedGroupDropdownFilter = selected.Count == 1
                ? GroupDropdownOptions.FirstOrDefault(g => g.Id == selected[0].Id) ?? GroupDropdownOptions[0]
                : GroupDropdownOptions.FirstOrDefault(g => g.Id is null);
        }
        finally
        {
            _updatingGroupFilter = false;
        }

        OnPropertyChanged(nameof(IsAllGroupsSelected));
        LoadCommand.Execute(null);
    }

    private void ApplyRowGrouping(bool isGrouped)
    {
        RowsView.GroupDescriptions.Clear();
        if (isGrouped)
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SalaryRowVm.GroupDisplayName)));
        RowsView.Refresh();
    }

    private void OnSalaryRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || TotalRefreshInputProperties.Contains(e.PropertyName))
            RefreshTotals();
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
            var b = SalaryCalculator.Compute(r.EffectiveBaseSalary, r.Year, r.Month, r.DaysAbsent,
                EffectiveEsicDeduction(r), EffectivePfDeduction(r), EffectiveTdsDeduction(r));
            return new MonthlySummaryRow(r.Name, r.EffectiveBaseSalary, r.DaysAbsent, b.Deduction,
                b.EsicDeduction, b.PfDeduction, b.TdsDeduction,
                Math.Max(0, r.AdvanceDeductionEntry), r.NetSalary);
        }).ToList();

    private static ValidationResult ValidateRows(IEnumerable<SalaryRowVm> rows, bool includeAdvance)
    {
        var rowList = rows.ToList();
        var issues = rowList
            .SelectMany(ValidateBaseSalaryOverride)
            .ToList();
        issues.AddRange(rowList
            .SelectMany(row => PayrollValidator.Validate(ToPayrollInput(row, includeAdvance)).Issues)
            .ToList());
        issues.AddRange(rowList
            .Where(row => !row.IsBaseSalaryOverrideEnabled && row.HistoricalNetSalaryOverride < 0)
            .Select(row => new ValidationIssue(row.Name, "Net salary override cannot be negative.", "net_override_negative")));
        return new ValidationResult(issues);
    }

    private static IEnumerable<ValidationIssue> ValidateBaseSalaryOverride(SalaryRowVm row)
    {
        if (!row.IsBaseSalaryOverrideEnabled)
            yield break;

        if (!row.BaseSalaryOverride.HasValue)
            yield return new ValidationIssue(row.Name, "Override base salary is required.", "base_override_required");
        else if (row.BaseSalaryOverride.Value < 0m)
            yield return new ValidationIssue(row.Name, "Override base salary cannot be negative.", "base_override_negative");
    }

    private static PayrollValidationInput ToPayrollInput(SalaryRowVm row, bool includeAdvance)
        => new(
            row.Name,
            row.EffectiveBaseSalary,
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

    private static decimal? EffectiveBaseSalaryOverride(SalaryRowVm row)
        => row.IsBaseSalaryOverrideEnabled && row.BaseSalaryOverride is decimal overrideSalary && overrideSalary >= 0m
            ? overrideSalary
            : null;

    private static List<ValidationIssue> ValidateIciciRows(IEnumerable<SalaryRowVm> rows)
    {
        var issues = new List<ValidationIssue>();
        foreach (var row in rows)
        {
            var employeeValidation = EmployeeValidator.Validate(new EmployeeValidationInput(
                row.Name,
                row.EmployeeBaseSalary,
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
    private sealed record AttendanceLoadState(int EmployeeId, decimal? BaseSalaryOverride);

    private static string ResolveSelectedGroupName(MonthlyPayrollRow row, IReadOnlyList<SalaryGroupSortOption> selectedGroups)
    {
        var membershipIds = row.Groups.Select(g => g.Id).ToHashSet();
        return selectedGroups.FirstOrDefault(g => membershipIds.Contains(g.Id))?.Name ?? "Ungrouped";
    }
}
