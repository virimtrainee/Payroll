using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SalaryManager.App.Helpers;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Converter;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.XlsIO;

namespace SalaryManager.App.Views;

public partial class SalarySheetView : UserControl
{
    private static readonly HashSet<string> GridTotalColumns = new(StringComparer.Ordinal)
    {
        nameof(SalaryRowVm.EmployeeBaseSalary),
        nameof(SalaryRowVm.DaysAbsent),
        nameof(SalaryRowVm.SalaryPaid),
        nameof(SalaryRowVm.PfDeduction),
        nameof(SalaryRowVm.EsicDeduction),
        nameof(SalaryRowVm.TdsDeduction),
        nameof(SalaryRowVm.TotalDeductions),
        nameof(SalaryRowVm.AdvanceDeductionEntry),
        nameof(SalaryRowVm.AdvanceBalance),
        nameof(SalaryRowVm.NetSalary)
    };

    private static readonly HashSet<string> GridWholeNumberTotalColumns = new(StringComparer.Ordinal)
    {
        nameof(SalaryRowVm.DaysAbsent),
        nameof(SalaryRowVm.SalaryPaid)
    };

    private bool _initialized;
    private bool _columnPreferencesReady;
    private bool _columnWidthHandlersAttached;
    private bool _applyingColumnWidths;
    private bool _columnWidthsDirty;
    private bool _columnWidthsApplied;
    private readonly DispatcherTimer _columnSaveTimer;
    private readonly DispatcherTimer _salarySearchTimer;
    private string _pendingSalarySearchText = string.Empty;
    private bool _suppressSalarySearchTextChanged;
    private SalaryGridContextCell? _pendingSalaryContextCell;
    private SalarySheetViewModel? _measuredViewModel;
    private Stopwatch? _gridRenderStopwatch;
    private int _gridRenderRowCount;
    private long _gridRenderSequence;

    public SalarySheetView()
    {
        InitializeComponent();
        SalaryGrid.SelectionController = new SalarySheetGridSelectionController(SalaryGrid);
        _columnSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _columnSaveTimer.Tick += (_, _) =>
        {
            _columnSaveTimer.Stop();
            SaveSalaryColumnPreferences();
        };
        _salarySearchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _salarySearchTimer.Tick += (_, _) =>
        {
            _salarySearchTimer.Stop();
            SalaryGrid.SearchHelper.Search(_pendingSalarySearchText);
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SalarySheetViewModel vm) return;

        SalaryGrid.SearchHelper.AllowFiltering = false;
        AttachColumnWidthHandlers();
        AttachGridRenderMeasurement(vm);
        ScheduleApplySavedColumnWidths(vm);

        if (!_initialized)
        {
            _initialized = true;
            UiCommandScheduler.ExecuteDeferredOnceIfPossible(vm.LoadCommand, dispatcherSource: this);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _columnSaveTimer.Stop();
        _salarySearchTimer.Stop();
        DetachGridRenderMeasurement();
        SaveSalaryColumnPreferences();
    }

    private void AttachGridRenderMeasurement(SalarySheetViewModel vm)
    {
        if (ReferenceEquals(_measuredViewModel, vm))
            return;

        DetachGridRenderMeasurement();
        _measuredViewModel = vm;
        vm.Rows.CollectionChanged += SalaryRows_CollectionChanged;
    }

    private void DetachGridRenderMeasurement()
    {
        if (_measuredViewModel is null)
            return;

        _measuredViewModel.Rows.CollectionChanged -= SalaryRows_CollectionChanged;
        _measuredViewModel = null;
    }

    private void SalaryRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Reset || _measuredViewModel is null)
            return;

        _gridRenderRowCount = _measuredViewModel.Rows.Count;
        _gridRenderStopwatch = Stopwatch.StartNew();
        var sequence = ++_gridRenderSequence;
        Dispatcher.BeginInvoke(
            () => LogSalaryGridRenderIdle(sequence),
            DispatcherPriority.ContextIdle);
    }

    private void LogSalaryGridRenderIdle(long sequence)
    {
        if (sequence != _gridRenderSequence || _gridRenderStopwatch is null)
            return;

        _gridRenderStopwatch.Stop();
        PerformanceTrace.SalaryGridRenderIdle(_gridRenderRowCount, _gridRenderStopwatch.Elapsed);
        _gridRenderStopwatch = null;
    }

    private void AttachColumnWidthHandlers()
    {
        if (_columnWidthHandlersAttached) return;

        var descriptor = DependencyPropertyDescriptor.FromProperty(
            GridColumn.WidthProperty,
            typeof(GridColumn));
        if (descriptor is null) return;

        foreach (var column in SalaryGrid.Columns)
        {
            descriptor.AddValueChanged(column, OnSalaryColumnWidthChanged);
        }

        _columnWidthHandlersAttached = true;
    }

    private void OnSalaryColumnWidthChanged(object? sender, EventArgs e)
    {
        if (!_columnPreferencesReady || _applyingColumnWidths) return;
        if (sender is not GridColumn column) return;
        if (string.IsNullOrWhiteSpace(DataGridColumnKey.GetKey(column))) return;
        if (!double.IsFinite(column.Width) || column.Width < 40) return;

        _columnWidthsDirty = true;
        _columnSaveTimer.Stop();
        _columnSaveTimer.Start();
    }

    private void ScheduleApplySavedColumnWidths(SalarySheetViewModel vm)
    {
        if (_columnWidthsApplied)
        {
            _columnPreferencesReady = true;
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            ApplySavedColumnPreferences(vm);
            _columnWidthsApplied = true;
            _columnPreferencesReady = true;
        }, DispatcherPriority.Background);
    }

    private void ApplySavedColumnPreferences(SalarySheetViewModel vm)
    {
        var widths = vm.LoadColumnWidths();
        if (widths.Count == 0) return;

        _applyingColumnWidths = true;
        SalaryGrid.Columns.Suspend();
        try
        {
            foreach (var column in SalaryGrid.Columns)
            {
                var key = DataGridColumnKey.GetKey(column);
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (widths.TryGetValue(key, out var width) && double.IsFinite(width) && width >= 40)
                {
                    var minimumWidth = Math.Max(column.MinimumWidth, 40);
                    column.Width = Math.Max(width, minimumWidth);
                }
            }
        }
        finally
        {
            SalaryGrid.Columns.Resume();
            SalaryGrid.RefreshColumns();
            _applyingColumnWidths = false;
        }
    }

    private void SaveSalaryColumnPreferences()
    {
        if (!_columnWidthsDirty) return;
        if (!_columnPreferencesReady) return;
        if (DataContext is not SalarySheetViewModel vm) return;

        var widths = new Dictionary<string, double>();
        foreach (var column in SalaryGrid.Columns)
        {
            var key = DataGridColumnKey.GetKey(column);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!double.IsFinite(column.Width) || column.Width < 40) continue;
            widths[key] = column.Width;
        }

        vm.SaveColumnWidths(widths);
        _columnWidthsDirty = false;
    }

    // Ctrl+S saves from anywhere inside the view, including while a cell is being edited
    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
        {
            ShowSalarySearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SalarySearchPopup.Visibility == Visibility.Visible)
        {
            HideSalarySearch(clearSearch: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
        {
            if (DataContext is SalarySheetViewModel vm)
            {
                SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
                vm.FlushPendingTotalRefresh();
                vm.SaveAttendanceCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private void ShowSalarySearch()
    {
        SalarySearchPopup.Visibility = Visibility.Visible;
        SalarySearchBox.Focus();
        SalarySearchBox.SelectAll();
    }

    private void HideSalarySearch(bool clearSearch)
    {
        if (clearSearch)
        {
            _salarySearchTimer.Stop();
            _pendingSalarySearchText = string.Empty;
            _suppressSalarySearchTextChanged = true;
            try
            {
                SalarySearchBox.Text = string.Empty;
            }
            finally
            {
                _suppressSalarySearchTextChanged = false;
            }
            SalaryGrid.SearchHelper.Search(string.Empty);
        }

        SalarySearchPopup.Visibility = Visibility.Collapsed;
        SalaryGrid.Focus();
    }

    private void SalarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSalarySearchTextChanged)
            return;

        _pendingSalarySearchText = SalarySearchBox.Text ?? string.Empty;
        _salarySearchTimer.Stop();
        _salarySearchTimer.Start();
    }

    private void SalarySearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideSalarySearch(clearSearch: true);
            e.Handled = true;
        }
    }

    private void CloseSalarySearch_Click(object sender, RoutedEventArgs e)
        => HideSalarySearch(clearSearch: true);

    // Auto-enter edit mode when a cell becomes current (only in edit mode)
    private void SalaryGrid_CurrentCellActivated(object sender, CurrentCellActivatedEventArgs e)
    {
        if (sender is not SfDataGrid dg || dg.IsReadOnly) return;
        Application.Current.Dispatcher.InvokeAsync(
            () => BeginEditAndFocus(dg),
            DispatcherPriority.Input);
    }

    private static void BeginEditAndFocus(SfDataGrid dg)
    {
        if (!IsEditableCell(dg.CurrentItem, dg.CurrentColumn)) return;

        dg.SelectionController.CurrentCellManager.BeginEdit();
    }

    private static bool IsEditableCell(object? item, GridColumn? column)
    {
        if (item is not SalaryRowVm row || column is null) return false;
        return SalarySheetGridNavigation.IsEditableColumnKey(row, DataGridColumnKey.GetKey(column));
    }

    private void CommitSalaryGridEdit_Click(object sender, RoutedEventArgs e)
    {
        SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void GridExcelButton_Click(object sender, RoutedEventArgs e)
    {
        SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
        if (DataContext is SalarySheetViewModel vm)
            vm.FlushPendingTotalRefresh();

        var selectedColumns = SelectGridExportColumns();
        if (selectedColumns is null)
            return;

        var defaultName = $"Salary-Grid-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        var dialog = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = defaultName
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ExportSalaryGrid(dialog.FileName, selectedColumns);
            OpenFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Grid export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<string>? SelectGridExportColumns()
    {
        var columns = SalaryGrid.Columns
            .Select(column => new GridExportColumn(
                column.MappingName,
                string.IsNullOrWhiteSpace(column.HeaderText) ? column.MappingName : column.HeaderText))
            .Where(column => !string.IsNullOrWhiteSpace(column.MappingName))
            .ToList();

        if (columns.Count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "There are no grid columns available to export.",
                "Grid export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        var checkBoxes = new List<CheckBox>(columns.Count);
        var owner = Window.GetWindow(this);
        var window = new Window
        {
            Title = "Export Grid Columns",
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxHeight = 560
        };

        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        window.Content = root;

        var title = new TextBlock
        {
            Text = "Select columns to export",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var actionBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        DockPanel.SetDock(actionBar, Dock.Bottom);
        root.Children.Add(actionBar);

        var selectBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(selectBar, Dock.Top);
        root.Children.Add(selectBar);

        var selectAllButton = new Button
        {
            Content = "All",
            MinWidth = 70,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource("SecondaryButton") as Style
        };
        selectAllButton.Click += (_, _) =>
        {
            foreach (var checkBox in checkBoxes)
                checkBox.IsChecked = true;
        };
        selectBar.Children.Add(selectAllButton);

        var selectNoneButton = new Button
        {
            Content = "None",
            MinWidth = 70,
            Padding = new Thickness(10, 5, 10, 5),
            Style = TryFindResource("SecondaryButton") as Style
        };
        selectNoneButton.Click += (_, _) =>
        {
            foreach (var checkBox in checkBoxes)
                checkBox.IsChecked = false;
        };
        selectBar.Children.Add(selectNoneButton);

        var columnPanel = new StackPanel();
        foreach (var column in columns)
        {
            var checkBox = new CheckBox
            {
                Content = column.Header,
                Tag = column.MappingName,
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13
            };
            checkBoxes.Add(checkBox);
            columnPanel.Children.Add(checkBox);
        }

        var scroller = new ScrollViewer
        {
            Content = columnPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360
        };
        root.Children.Add(scroller);

        var cancelButton = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 82,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource("SecondaryButton") as Style
        };
        actionBar.Children.Add(cancelButton);

        var exportButton = new Button
        {
            Content = "Export",
            IsDefault = true,
            MinWidth = 82,
            Padding = new Thickness(12, 6, 12, 6),
            Style = TryFindResource("PrimaryButton") as Style
        };
        exportButton.Click += (_, _) =>
        {
            if (checkBoxes.All(checkBox => checkBox.IsChecked != true))
            {
                MessageBox.Show(
                    window,
                    "Select at least one column to export.",
                    "Grid export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            window.DialogResult = true;
        };
        actionBar.Children.Add(exportButton);

        if (window.ShowDialog() != true)
            return null;

        return checkBoxes
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => checkBox.Tag as string)
            .Where(mappingName => !string.IsNullOrWhiteSpace(mappingName))
            .Cast<string>()
            .ToList();
    }

    private void ExportSalaryGrid(string path, IEnumerable<string> columnMappings)
    {
        var selectedMappings = columnMappings.ToList();
        var options = new ExcelExportingOptions
        {
            AllowOutlining = true,
            ExportMode = ExportMode.Text,
            ExportStackedHeaders = false,
            ExcelVersion = ExcelVersion.Xlsx
        };
        options.Columns.AddRange(selectedMappings);

        using (var engine = SalaryGrid.ExportToExcel(SalaryGrid.View, options))
        {
            engine.Excel.Workbooks[0].SaveAs(path);
        }

        AppendSalaryGridTotals(path, selectedMappings, GetVisibleSalaryRows());
    }

    private IReadOnlyList<SalaryRowVm> GetVisibleSalaryRows()
    {
        var rows = new List<SalaryRowVm>();
        if (SalaryGrid.View?.Records is not null)
        {
            foreach (var record in SalaryGrid.View.Records)
            {
                if (record.Data is SalaryRowVm entryRow)
                {
                    rows.Add(entryRow);
                }
            }
        }

        if (rows.Count > 0)
            return rows;

        return DataContext is SalarySheetViewModel vm
            ? vm.RowsView.Cast<SalaryRowVm>().ToList()
            : [];
    }

    private static void AppendSalaryGridTotals(
        string path,
        IReadOnlyList<string> columnMappings,
        IReadOnlyList<SalaryRowVm> rows)
    {
        if (columnMappings.Count == 0 || rows.Count == 0)
            return;

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        var ws = workbook.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 1)
        {
            workbook.SaveAs(path);
            return;
        }

        var totalRow = lastRow + 1;
        var labelColumn = columnMappings
            .Select((mapping, index) => new { mapping, index })
            .FirstOrDefault(item => !GridTotalColumns.Contains(item.mapping))?.index + 1;

        for (var c = 0; c < columnMappings.Count; c++)
        {
            var cell = ws.Cell(totalRow, c + 1);
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#2563EB");
            cell.Style.Border.TopBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

            if (labelColumn.HasValue && c + 1 == labelColumn.Value)
            {
                cell.Value = "TOTAL";
                continue;
            }

            var mapping = columnMappings[c];
            if (!GridTotalColumns.Contains(mapping))
                continue;

            var total = rows.Sum(row => GetGridTotalValue(row, mapping));
            cell.Value = total;
            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;
            cell.Style.NumberFormat.Format = GridWholeNumberTotalColumns.Contains(mapping) ? "#,##0" : "#,##0.00";
        }

        workbook.SaveAs(path);
    }

    private static decimal GetGridTotalValue(SalaryRowVm row, string mapping)
        => mapping switch
        {
            nameof(SalaryRowVm.EmployeeBaseSalary) => row.EmployeeBaseSalary,
            nameof(SalaryRowVm.DaysAbsent) => row.DaysAbsent,
            nameof(SalaryRowVm.SalaryPaid) => row.SalaryPaid ?? 0m,
            nameof(SalaryRowVm.PfDeduction) => row.PfDeduction,
            nameof(SalaryRowVm.EsicDeduction) => row.EsicDeduction,
            nameof(SalaryRowVm.TdsDeduction) => row.TdsDeduction,
            nameof(SalaryRowVm.TotalDeductions) => row.TotalDeductions,
            nameof(SalaryRowVm.AdvanceDeductionEntry) => row.AdvanceDeductionEntry,
            nameof(SalaryRowVm.AdvanceBalance) => row.AdvanceBalance,
            nameof(SalaryRowVm.NetSalary) => row.NetSalary,
            _ => 0m
        };

    private void SalaryGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pendingSalaryContextCell = null;
        if (TryResolveSalaryGridCell(e, out var row, out var column, out var rowIndex))
            _pendingSalaryContextCell = new SalaryGridContextCell(row, column, rowIndex);
    }

    private bool TryResolveSalaryGridCell(
        MouseButtonEventArgs e,
        out SalaryRowVm row,
        out GridColumn column,
        out int rowIndex)
    {
        row = null!;
        column = null!;
        rowIndex = -1;

        var visualContainer = SalaryGrid.GetVisualContainer();
        var rowColumnIndex = visualContainer.PointToCellRowColumnIndex(e.GetPosition(visualContainer), true);
        if (rowColumnIndex.RowIndex < 0 || rowColumnIndex.ColumnIndex < 0)
            return false;

        if (SelectionHelper.GetRecordAtRowIndex(SalaryGrid, rowColumnIndex.RowIndex) is not SalaryRowVm salaryRow)
            return false;

        var columnIndex = SalaryGrid.ResolveToGridVisibleColumnIndex(rowColumnIndex.ColumnIndex);
        if (columnIndex < 0 || columnIndex >= SalaryGrid.Columns.Count)
            return false;

        if (SalaryGrid.Columns[columnIndex] is not GridColumn gridColumn
            || string.IsNullOrWhiteSpace(gridColumn.MappingName))
            return false;

        row = salaryRow;
        column = gridColumn;
        rowIndex = rowColumnIndex.RowIndex;
        return true;
    }

    private void SalaryGridContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var hasTarget = HasSalaryContextTarget();
        var canBulkEdit = hasTarget
                          && DataContext is SalarySheetViewModel { IsEditMode: true };

        ExportSelectionPdfMenuItem.IsEnabled = hasTarget;
        ExportSelectionExcelMenuItem.IsEnabled = hasTarget;
        FillSalaryPaidBaseMenuItem.IsEnabled = canBulkEdit;
        ClearSalaryPaidMenuItem.IsEnabled = canBulkEdit;
        ZeroDeductionsMenuItem.IsEnabled = canBulkEdit;
        SetAdvanceDeductionMenuItem.IsEnabled = canBulkEdit;
    }

    private void SalaryGridContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _pendingSalaryContextCell = null;
    }

    private async void ExportSelectionPdf_Click(object sender, RoutedEventArgs e)
    {
        SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
        if (DataContext is not SalarySheetViewModel vm)
            return;

        vm.FlushPendingTotalRefresh();

        var snapshot = GetSalaryGridSelectionSnapshotForAction();
        if (!snapshot.HasSelection)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Select at least one salary-grid cell to export.",
                "Export Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"Salary-Selection-{DateTime.Now:yyyyMMdd-HHmm}.pdf"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await vm.ExportSelectionPdfAsync(dialog.FileName, snapshot);
            OpenFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Selected PDF export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportSelectionExcel_Click(object sender, RoutedEventArgs e)
    {
        SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
        if (DataContext is not SalarySheetViewModel vm)
            return;

        vm.FlushPendingTotalRefresh();

        var snapshot = GetSalaryGridSelectionSnapshotForAction();
        if (!snapshot.HasSelection)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Select at least one salary-grid cell to export.",
                "Export Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"Salary-Selection-{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await vm.ExportSelectionExcelAsync(dialog.FileName, snapshot);
            OpenFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Selected Excel export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FillSalaryPaidBase_Click(object sender, RoutedEventArgs e)
        => ApplySelectionBulkEdit((vm, snapshot) => vm.ResetSalaryPaidToCalculated(snapshot.Rows));

    private void ClearSalaryPaid_Click(object sender, RoutedEventArgs e)
        => ApplySelectionBulkEdit((vm, snapshot) => vm.ClearSalaryPaid(snapshot.Rows));

    private void ZeroDeductions_Click(object sender, RoutedEventArgs e)
        => ApplySelectionBulkEdit((vm, snapshot) => vm.ZeroDeductions(snapshot.Rows, snapshot.ColumnKeys));

    private async void SetAdvanceDeduction_Click(object sender, RoutedEventArgs e)
    {
        SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
        if (DataContext is not SalarySheetViewModel vm || !vm.IsEditMode)
            return;

        var snapshot = GetSalaryGridSelectionSnapshotForAction();
        if (!snapshot.HasSelection)
            return;

        await vm.PromptAndApplyAdvanceDeductionAsync(snapshot.Rows);
    }

    private void ApplySelectionBulkEdit(Action<SalarySheetViewModel, SalarySheetSelectionSnapshot> action)
    {
        SalaryGrid.SelectionController.CurrentCellManager.EndEdit(true);
        if (DataContext is not SalarySheetViewModel vm || !vm.IsEditMode)
            return;

        var snapshot = GetSalaryGridSelectionSnapshotForAction();
        if (!snapshot.HasSelection)
            return;

        action(vm, snapshot);
    }

    private bool HasSalaryContextTarget()
    {
        if (_pendingSalaryContextCell is { } pendingCell
            && IsActionableSalaryGridCell(pendingCell))
        {
            return true;
        }

        return SalarySheetGridSelection.HasCurrentGridCell(SalaryGrid);
    }

    private SalarySheetSelectionSnapshot GetSalaryGridSelectionSnapshotForAction()
    {
        var snapshot = SalarySheetGridSelection.FromGrid(SalaryGrid);
        if (_pendingSalaryContextCell is not { } pendingCell
            || !IsActionableSalaryGridCell(pendingCell)
            || snapshot.Contains(pendingCell.Row, pendingCell.Column.MappingName))
        {
            return snapshot;
        }

        return SalarySheetGridSelection.FromSingleCell(
            SalaryGrid,
            pendingCell.Row,
            pendingCell.Column,
            pendingCell.RowIndex);
    }

    private static bool IsActionableSalaryGridCell(SalaryGridContextCell cell)
        => !cell.Column.IsHidden
           && !string.IsNullOrWhiteSpace(cell.Column.MappingName)
           && !string.IsNullOrWhiteSpace(DataGridColumnKey.GetKey(cell.Column));

    private static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private sealed record GridExportColumn(string MappingName, string Header);
    private sealed record SalaryGridContextCell(SalaryRowVm Row, GridColumn Column, int RowIndex);
}
