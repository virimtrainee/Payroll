using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    public decimal BaseSalary => EmployeeBaseSalary;
    public decimal EffectiveBaseSalary => EmployeeBaseSalary;
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
    public decimal DefaultSalaryPaid
    {
        get
        {
            if (EmployeeBaseSalary < 0m || Year < 1 || Month is < 1 or > 12)
                return EmployeeBaseSalary;

            return SalaryCalculator.CalculateDefaultSalaryPaid(
                EmployeeBaseSalary,
                Year,
                Month,
                Math.Max(0, DaysAbsent));
        }
    }
    public decimal EffectiveSalaryPaid
        => IsBaseSalaryOverrideEnabled && BaseSalaryOverride is decimal overrideSalary && overrideSalary >= 0m
            ? overrideSalary
            : DefaultSalaryPaid;
    public bool UsesTds => !IsCashDeductionsDisabled && EffectiveSalaryPaid > SalaryCalculator.TdsThreshold;
    public bool UsesEsicPf => !IsCashDeductionsDisabled && !UsesTds;
    public decimal TotalDeductions => Math.Max(0, EffectiveSalaryPaid - NetSalary);
    public bool HasInvalidBaseSalaryOverride => IsBaseSalaryOverrideEnabled
        && (!BaseSalaryOverride.HasValue || BaseSalaryOverride.Value < 0m);
    public string SalaryPaidValidationMessage
        => !IsBaseSalaryOverrideEnabled
            ? string.Empty
            : !BaseSalaryOverride.HasValue
                ? "Salary paid is required."
                : BaseSalaryOverride.Value < 0m
                    ? "Salary paid cannot be negative."
                    : string.Empty;
    public decimal? SalaryPaid
    {
        get => IsBaseSalaryOverrideEnabled ? BaseSalaryOverride : DefaultSalaryPaid;
        set
        {
            if (value.HasValue)
            {
                var salaryPaid = NormalizeSalaryPaid(value.Value);
                if (salaryPaid == DefaultSalaryPaid)
                {
                    if (IsBaseSalaryOverrideEnabled)
                    {
                        IsBaseSalaryOverrideEnabled = false;
                    }
                    else if (BaseSalaryOverride.HasValue)
                    {
                        BaseSalaryOverride = null;
                    }
                    else
                    {
                        OnPropertyChanged(nameof(SalaryPaid));
                    }

                    return;
                }

                BaseSalaryOverride = salaryPaid;
                if (!IsBaseSalaryOverrideEnabled)
                    IsBaseSalaryOverrideEnabled = true;
                else
                    OnPropertyChanged(nameof(SalaryPaid));
                return;
            }

            if (IsBaseSalaryOverrideEnabled)
            {
                IsBaseSalaryOverrideEnabled = false;
            }
            else if (BaseSalaryOverride.HasValue)
            {
                BaseSalaryOverride = null;
            }
            else
            {
                OnPropertyChanged(nameof(SalaryPaid));
            }
        }
    }
    private int _recalculationSuppression;
    private bool _updatingAutomaticTds;
    private bool IsRecalculationSuppressed => _recalculationSuppression > 0;

    // Editable attendance fields — trigger recalculation on change
    [ObservableProperty] private int daysAbsent;
    [ObservableProperty] private decimal esicDeduction;
    [ObservableProperty] private decimal pfDeduction;
    [ObservableProperty] private decimal tdsDeduction;
    [ObservableProperty] private bool isTdsManualOverride;
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
        decimal advanceDeductionEntry,
        bool isTdsManualOverride = false)
    {
        _recalculationSuppression++;
        try
        {
            DaysAbsent = daysAbsent;
            var hasCustomSalaryPaid = isBaseSalaryOverrideEnabled
                && baseSalaryOverride.HasValue
                && NormalizeSalaryPaid(baseSalaryOverride.Value) != DefaultSalaryPaid;

            BaseSalaryOverride = hasCustomSalaryPaid ? NormalizeSalaryPaid(baseSalaryOverride!.Value) : null;
            IsBaseSalaryOverrideEnabled = hasCustomSalaryPaid;
            EsicDeduction = esicDeduction;
            PfDeduction = pfDeduction;
            IsTdsManualOverride = UsesTds && isTdsManualOverride;
            SetAutomaticTds(UsesTds && IsTdsManualOverride
                ? Math.Max(0m, tdsDeduction)
                : UsesTds
                    ? SalaryCalculator.CalculateDefaultTds(EffectiveSalaryPaid)
                    : 0m);
            AdvanceDeductionEntry = advanceDeductionEntry;
        }
        finally
        {
            _recalculationSuppression--;
        }

        RefreshSalaryBasisAndRecalculate();
    }

    partial void OnDaysAbsentChanged(int value)
    {
        OnPropertyChanged(nameof(DefaultSalaryPaid));
        if (!IsBaseSalaryOverrideEnabled)
            OnPropertyChanged(nameof(SalaryPaid));
        RefreshSalaryBasisIfNotSuppressed();
    }
    partial void OnEsicDeductionChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnPfDeductionChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnTdsDeductionChanged(decimal value)
    {
        if (!IsRecalculationSuppressed && !_updatingAutomaticTds)
        {
            IsTdsManualOverride = UsesTds
                && value != SalaryCalculator.CalculateDefaultTds(EffectiveSalaryPaid);
        }

        RecalculateIfNotSuppressed();
    }
    partial void OnAdvanceDeductionEntryChanged(decimal value) => RecalculateIfNotSuppressed();
    partial void OnIsBaseSalaryOverrideEnabledChanged(bool value)
    {
        if (!IsRecalculationSuppressed)
        {
            if (value && !BaseSalaryOverride.HasValue)
                BaseSalaryOverride = DefaultSalaryPaid;
            if (!value)
                BaseSalaryOverride = null;
        }

        OnSalaryBasisChanged();
        OnPropertyChanged(nameof(SalaryPaid));
    }

    partial void OnBaseSalaryOverrideChanged(decimal? value)
    {
        OnPropertyChanged(nameof(SalaryPaid));
        OnSalaryBasisChanged();
    }

    private void OnSalaryBasisChanged()
    {
        if (IsRecalculationSuppressed)
            return;

        RefreshSalaryBasisAndRecalculate();
    }

    private void RefreshSalaryBasisIfNotSuppressed()
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
        OnPropertyChanged(nameof(DefaultSalaryPaid));
        OnPropertyChanged(nameof(EffectiveSalaryPaid));
        OnPropertyChanged(nameof(SalaryPaid));
        OnPropertyChanged(nameof(UsesTds));
        OnPropertyChanged(nameof(UsesEsicPf));
        OnPropertyChanged(nameof(TotalDeductions));
        OnPropertyChanged(nameof(HasInvalidBaseSalaryOverride));
        OnPropertyChanged(nameof(SalaryPaidValidationMessage));
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
            if (!IsTdsManualOverride)
                SetAutomaticTds(SalaryCalculator.CalculateDefaultTds(EffectiveSalaryPaid));
        }
        else
        {
            if (IsTdsManualOverride) IsTdsManualOverride = false;
            SetAutomaticTds(0m);
        }
    }

    private void SetAutomaticTds(decimal value)
    {
        _updatingAutomaticTds = true;
        try
        {
            TdsDeduction = value;
        }
        finally
        {
            _updatingAutomaticTds = false;
        }
    }

    public void Recalculate()
    {
        if (HasInvalidBaseSalaryOverride)
        {
            OnPropertyChanged(nameof(TotalDeductions));
            return;
        }

        var b = SalaryCalculator.ComputeFromSalaryPaid(EmployeeBaseSalary, EffectiveSalaryPaid, Year, Month,
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
        OnPropertyChanged(nameof(DefaultSalaryPaid));
        OnPropertyChanged(nameof(EffectiveSalaryPaid));
        OnPropertyChanged(nameof(SalaryPaid));
        OnPropertyChanged(nameof(TotalDeductions));
    }

    private static decimal NormalizeSalaryPaid(decimal salaryPaid)
        => salaryPaid < 0m ? salaryPaid : SalaryCalculator.RoundSalaryPaid(salaryPaid);
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
    private readonly object _totalRefreshLock = new();
    private readonly SynchronizationContext? _totalRefreshSynchronizationContext = SynchronizationContext.Current;
    private CancellationTokenSource? _totalRefreshCts;
    private int _totalRefreshSuppression;
    private bool _suppressedTotalRefreshRequested;

    public static TimeSpan TotalRefreshDebounceDelay { get; } = TimeSpan.FromMilliseconds(150);

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

    private int _loadVersion;
    private static readonly HashSet<string> TotalRefreshInputProperties = new(StringComparer.Ordinal)
    {
        nameof(SalaryRowVm.DaysAbsent),
        nameof(SalaryRowVm.EsicDeduction),
        nameof(SalaryRowVm.PfDeduction),
        nameof(SalaryRowVm.TdsDeduction),
        nameof(SalaryRowVm.AdvanceDeductionEntry),
        nameof(SalaryRowVm.IsBaseSalaryOverrideEnabled),
        nameof(SalaryRowVm.BaseSalaryOverride),
        nameof(SalaryRowVm.SalaryPaid)
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
    }

    partial void OnSelectedMonthChanged(MonthOption value) => LoadCommand.Execute(null);
    partial void OnSelectedYearChanged(int value) => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task ToggleEditModeAsync()
    {
        if (!IsEditMode)
        {
            IsEditMode = true;
            return;
        }

        FlushPendingTotalRefresh();
        if (await SaveAttendanceCoreAsync(showSuccessMessage: false))
            IsEditMode = false;
    }

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

    public async Task ExportSelectionPdfAsync(string path, SalarySheetSelectionSnapshot snapshot)
    {
        if (!snapshot.HasSelection)
            throw new InvalidOperationException("Select at least one salary-grid cell to export.");

        var export = BuildSelectionExport(snapshot);
        var year = SelectedYear;
        var month = SelectedMonth.Number;

        await Task.Run(() => _pdf.GenerateSalarySheetSelection(year, month, export.Columns, export.Rows, path));
    }

    public async Task ExportSelectionExcelAsync(string path, SalarySheetSelectionSnapshot snapshot)
    {
        if (!snapshot.HasSelection)
            throw new InvalidOperationException("Select at least one salary-grid cell to export.");

        var export = BuildSelectionExport(snapshot);
        var year = SelectedYear;
        var month = SelectedMonth.Number;

        await Task.Run(() => _excel.ExportSalarySheetSelection(year, month, export.Columns, export.Rows, path));
    }

    public void ResetSalaryPaidToCalculated(IEnumerable<SalaryRowVm> rows)
        => RunBulkEdit(rows, row => row.SalaryPaid = null);

    public void FillSalaryPaidWithBase(IEnumerable<SalaryRowVm> rows)
        => ResetSalaryPaidToCalculated(rows);

    public void ClearSalaryPaid(IEnumerable<SalaryRowVm> rows)
        => RunBulkEdit(rows, row => row.SalaryPaid = null);

    public void ZeroDeductions(IEnumerable<SalaryRowVm> rows, IEnumerable<string> selectedColumnKeys)
    {
        var deductionKeys = selectedColumnKeys
            .Where(SalarySheetGridSelection.IsDeductionColumnKey)
            .ToHashSet(StringComparer.Ordinal);
        if (deductionKeys.Count == 0)
        {
            deductionKeys.Add("esic");
            deductionKeys.Add("pf");
            deductionKeys.Add("tds");
            deductionKeys.Add("advanceDeduction");
        }

        RunBulkEdit(rows, row =>
        {
            if (deductionKeys.Contains("esic"))
                row.EsicDeduction = 0m;
            if (deductionKeys.Contains("pf"))
                row.PfDeduction = 0m;
            if (deductionKeys.Contains("tds"))
                row.TdsDeduction = 0m;
            if (deductionKeys.Contains("advanceDeduction"))
                row.AdvanceDeductionEntry = 0m;
        });
    }

    public bool ApplyAdvanceDeduction(IEnumerable<SalaryRowVm> rows, decimal amount)
    {
        if (amount < 0m)
            return false;

        RunBulkEdit(rows, row => row.AdvanceDeductionEntry = amount);
        return true;
    }

    public async Task PromptAndApplyAdvanceDeductionAsync(IEnumerable<SalaryRowVm> rows)
    {
        var rowList = rows.Distinct().ToList();
        if (!IsEditMode || rowList.Count == 0)
            return;

        var amount = await _dialogs.AskNonNegativeDecimalAsync(
            "Set Advance Deduction",
            "Advance deduction",
            0m,
            "Apply");
        if (!amount.HasValue)
            return;

        if (!ApplyAdvanceDeduction(rowList, amount.Value))
            await _dialogs.ErrorAsync("Advance deduction cannot be negative.", "Invalid amount");
    }

    private void RunBulkEdit(IEnumerable<SalaryRowVm> rows, Action<SalaryRowVm> edit)
    {
        if (!IsEditMode)
            return;

        var rowList = rows.Distinct().ToList();
        if (rowList.Count == 0)
            return;

        _totalRefreshSuppression++;
        try
        {
            foreach (var row in rowList)
                edit(row);
        }
        finally
        {
            _totalRefreshSuppression--;
            if (_totalRefreshSuppression == 0 && _suppressedTotalRefreshRequested)
            {
                _suppressedTotalRefreshRequested = false;
                ScheduleTotalRefresh();
            }
        }
    }

    [RelayCommand]
    private async Task ResetTableAsync()
    {
        FlushPendingTotalRefresh();
        var shouldReset = await _dialogs.ConfirmWarningAsync(
            "Reset the salary table? Unsaved changes in this table will be discarded.",
            "Reset Table",
            "Reset");
        if (!shouldReset)
            return;

        await LoadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var totalStopwatch = Stopwatch.StartNew();
        TimeSpan payrollElapsed = TimeSpan.Zero;
        TimeSpan attendanceElapsed = TimeSpan.Zero;
        TimeSpan rowBuildElapsed = TimeSpan.Zero;
        TimeSpan rowsReplaceElapsed = TimeSpan.Zero;
        var rowCount = 0;
        var version = Interlocked.Increment(ref _loadVersion);
        var year = SelectedYear;
        var month = SelectedMonth.Number;
        IsLoading = true;
        try
        {
            await _dbInit.ReadyTask;

            using var db = await _dbf.CreateDbContextAsync();

            var stepStopwatch = Stopwatch.StartNew();
            var snapshot = await _payroll.LoadAsync(new MonthlyPayrollRequest(year, month));
            payrollElapsed = stepStopwatch.Elapsed;
            if (version != _loadVersion) return;

            stepStopwatch.Restart();
            var attendanceStates = await LoadAttendanceStatesAsync(
                db,
                snapshot.Rows.Select(r => r.EmployeeId).ToList(),
                year,
                month);
            attendanceElapsed = stepStopwatch.Elapsed;
            if (version != _loadVersion) return;

            var rowGroups = snapshot.Rows.ToDictionary(
                r => r.EmployeeId,
                r => r.PrimaryGroupName);

            stepStopwatch.Restart();
            var payrollRows = snapshot.Rows.OrderBy(r => r.Name).ToList();

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
                    payrollRow.AdvanceDeduction,
                    attendanceState?.IsTdsManualOverride ?? payrollRow.IsTdsManualOverride);

                newRows.Add(row);
            }
            rowBuildElapsed = stepStopwatch.Elapsed;

            if (version != _loadVersion) return;
            foreach (var row in Rows)
                row.PropertyChanged -= OnSalaryRowPropertyChanged;
            foreach (var row in newRows)
                row.PropertyChanged += OnSalaryRowPropertyChanged;
            stepStopwatch.Restart();
            using (RowsView.DeferRefresh())
            {
                Rows.ReplaceAll(newRows);
            }
            rowsReplaceElapsed = stepStopwatch.Elapsed;
            rowCount = newRows.Count;

            CancelPendingTotalRefresh();
            RefreshTotals();
            PerformanceTrace.SalarySheetLoad(
                year,
                month,
                rowCount,
                totalStopwatch.Elapsed,
                payrollElapsed,
                attendanceElapsed,
                rowBuildElapsed,
                rowsReplaceElapsed);
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
        FlushPendingTotalRefresh();
        await SaveAttendanceCoreAsync(showSuccessMessage: true);
    }

    private async Task<bool> SaveAttendanceCoreAsync(bool showSuccessMessage)
    {
        FlushPendingTotalRefresh();
        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            var validation = ValidateRows(Rows, includeAdvance: false);
            if (!validation.IsValid) { await _dialogs.ErrorAsync(validation.ToMessage()); return false; }

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
                    rec.IsTdsManualOverride = r.UsesTds && r.IsTdsManualOverride;
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
                        IsTdsManualOverride = r.UsesTds && r.IsTdsManualOverride,
                        BaseSalaryOverride = EffectiveBaseSalaryOverride(r),
                        NetSalaryOverride = null,
                    });
                }
            }

            await db.SaveChangesAsync();
            if (showSuccessMessage)
                await _dialogs.InfoAsync("Attendance saved.");
            return true;
        }
        catch (Exception ex) { await _dialogs.ErrorAsync(ex.Message); return false; }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        FlushPendingTotalRefresh();
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
        FlushPendingTotalRefresh();
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
        FlushPendingTotalRefresh();
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
        FlushPendingTotalRefresh();
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
            .Select(a => new AttendanceLoadState(a.EmployeeId, a.BaseSalaryOverride, a.IsTdsManualOverride))
            .ToDictionaryAsync(a => a.EmployeeId);
    }

    private void OnSalaryRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || TotalRefreshInputProperties.Contains(e.PropertyName))
            ScheduleTotalRefresh();
    }

    public void FlushPendingTotalRefresh()
    {
        CancelPendingTotalRefresh();
        RefreshTotals();
    }

    private void ScheduleTotalRefresh()
    {
        if (_totalRefreshSuppression > 0)
        {
            _suppressedTotalRefreshRequested = true;
            return;
        }

        CancellationTokenSource cts;
        lock (_totalRefreshLock)
        {
            _totalRefreshCts?.Cancel();
            _totalRefreshCts?.Dispose();
            _totalRefreshCts = new CancellationTokenSource();
            cts = _totalRefreshCts;
        }

        _ = RefreshTotalsAfterDelayAsync(cts, cts.Token);
    }

    private async Task RefreshTotalsAfterDelayAsync(CancellationTokenSource cts, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TotalRefreshDebounceDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_totalRefreshLock)
        {
            if (!ReferenceEquals(_totalRefreshCts, cts))
                return;

            _totalRefreshCts = null;
        }

        cts.Dispose();
        DispatchRefreshTotals();
    }

    private void DispatchRefreshTotals()
    {
        if (_totalRefreshSynchronizationContext is null)
        {
            RefreshTotals();
            return;
        }

        _totalRefreshSynchronizationContext.Post(_ => RefreshTotals(), null);
    }

    private void CancelPendingTotalRefresh()
    {
        CancellationTokenSource? cts;
        lock (_totalRefreshLock)
        {
            cts = _totalRefreshCts;
            _totalRefreshCts = null;
        }

        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    private void RefreshTotals()
    {
        TotalNet = Rows.Sum(r => r.NetSalary);
        TotalGross = Rows.Sum(r => r.EffectiveSalaryPaid);
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
            var b = SalaryCalculator.ComputeFromSalaryPaid(r.EmployeeBaseSalary, r.EffectiveSalaryPaid, r.Year, r.Month, r.DaysAbsent,
                EffectiveEsicDeduction(r), EffectivePfDeduction(r), EffectiveTdsDeduction(r));
            return new MonthlySummaryRow(r.Name, r.EffectiveBaseSalary, b.SalaryPaid, r.DaysAbsent, b.Deduction,
                b.EsicDeduction, b.PfDeduction, b.TdsDeduction,
                Math.Max(0, r.AdvanceDeductionEntry), r.NetSalary);
        }).ToList();

    private static SalarySheetSelectionExport BuildSelectionExport(SalarySheetSelectionSnapshot snapshot)
    {
        var columns = snapshot.Columns
            .Select(column => new SalarySheetSelectionColumn(
                column.Header,
                IsRightAlignedSelectionColumn(column.Key)))
            .ToList();
        var rows = snapshot.Rows
            .Select(row => new SalarySheetSelectionRow(
                snapshot.Columns
                    .Select(column => FormatSelectionValue(row, column.Key))
                    .ToList()))
            .ToList();

        return new SalarySheetSelectionExport(columns, rows);
    }

    private static bool IsRightAlignedSelectionColumn(string key)
        => key is "serial"
            or "baseSalary"
            or "absent"
            or "salaryPaid"
            or "esic"
            or "pf"
            or "tds"
            or "deductions"
            or "advanceDeduction"
            or "advanceBalance"
            or "netSalary";

    private static string FormatSelectionValue(SalaryRowVm row, string key)
        => key switch
        {
            "serial" => row.SerialNumber.ToString(CultureInfo.CurrentCulture),
            "employee" => row.Name,
            "group" => row.GroupDisplayName,
            "paymentMode" => row.PaymentModeLabel,
            "baseSalary" => FormatMoney(row.EmployeeBaseSalary),
            "absent" => row.DaysAbsent.ToString(CultureInfo.CurrentCulture),
            "salaryPaid" => FormatWholeMoney(row.SalaryPaid),
            "esic" => FormatMoney(row.EsicDeduction),
            "pf" => FormatMoney(row.PfDeduction),
            "tds" => FormatMoney(row.TdsDeduction),
            "deductions" => FormatMoney(row.TotalDeductions),
            "advanceDeduction" => FormatMoney(row.AdvanceDeductionEntry),
            "advanceBalance" => FormatMoney(row.AdvanceBalance),
            "netSalary" => FormatMoney(row.NetSalary),
            _ => string.Empty
        };

    private static string FormatMoney(decimal value)
        => value.ToString("N2", CultureInfo.CurrentCulture);

    private static string FormatMoney(decimal? value)
        => value.HasValue ? FormatMoney(value.Value) : string.Empty;

    private static string FormatWholeMoney(decimal? value)
        => value.HasValue ? value.Value.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;

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
            yield return new ValidationIssue(row.Name, "Salary paid is required.", "base_override_required");
        else if (row.BaseSalaryOverride.Value < 0m)
            yield return new ValidationIssue(row.Name, "Salary paid cannot be negative.", "base_override_negative");
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
            row.ExistingSalaryAdvanceDeduction,
            row.IsBaseSalaryOverrideEnabled ? row.BaseSalaryOverride : row.DefaultSalaryPaid);

    private static decimal EffectiveEsicDeduction(SalaryRowVm row)
        => row.UsesEsicPf ? Math.Max(0, row.EsicDeduction) : 0m;

    private static decimal EffectivePfDeduction(SalaryRowVm row)
        => row.UsesEsicPf ? Math.Max(0, row.PfDeduction) : 0m;

    private static decimal EffectiveTdsDeduction(SalaryRowVm row)
        => row.UsesTds ? Math.Max(0, row.TdsDeduction) : 0m;

    private static decimal? EffectiveBaseSalaryOverride(SalaryRowVm row)
        => row.IsBaseSalaryOverrideEnabled
            && row.BaseSalaryOverride is decimal overrideSalary
            && overrideSalary >= 0m
            && SalaryCalculator.RoundSalaryPaid(overrideSalary) != row.DefaultSalaryPaid
            ? SalaryCalculator.RoundSalaryPaid(overrideSalary)
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

    private sealed record SalarySheetSelectionExport(
        IReadOnlyList<SalarySheetSelectionColumn> Columns,
        IReadOnlyList<SalarySheetSelectionRow> Rows);

    private sealed record AttendanceLoadState(int EmployeeId, decimal? BaseSalaryOverride, bool IsTdsManualOverride);
}
