using System;
using System.Collections.Generic;
using System.Linq;
using SalaryManager.App.ViewModels;
using Syncfusion.UI.Xaml.Grid;

namespace SalaryManager.App.Helpers;

public sealed record SalarySheetColumnSelection(string Key, string MappingName, string Header);

public sealed record SalarySheetCellSelection(SalaryRowVm Row, string ColumnKey, int RowOrder);

public sealed record SalarySheetSelectionSnapshot(
    IReadOnlyList<SalaryRowVm> Rows,
    IReadOnlyList<SalarySheetColumnSelection> Columns)
{
    public bool HasSelection => Rows.Count > 0 && Columns.Count > 0;
    public IReadOnlyList<string> ColumnKeys => Columns.Select(column => column.Key).ToArray();
    public IReadOnlyList<string> MappingNames => Columns.Select(column => column.MappingName).ToArray();

    public bool Contains(SalaryRowVm row, string mappingName)
        => Rows.Contains(row)
           && Columns.Any(column => string.Equals(column.MappingName, mappingName, StringComparison.Ordinal));
}

public static class SalarySheetGridSelection
{
    private static readonly HashSet<string> DeductionColumnKeys = new(StringComparer.Ordinal)
    {
        "esic",
        "pf",
        "tds",
        "advanceDeduction"
    };

    public static bool IsDeductionColumnKey(string key) => DeductionColumnKeys.Contains(key);

    public static SalarySheetSelectionSnapshot FromGrid(SfDataGrid dataGrid)
    {
        var columns = GetVisibleColumnSelections(dataGrid);

        var selectedCells = dataGrid.GetSelectedCells()
            .Where(cell => cell.IsDataRowCell
                           && cell.RowData is SalaryRowVm
                           && cell.Column is GridColumn)
            .Select(cell => new SalarySheetCellSelection(
                (SalaryRowVm)cell.RowData,
                DataGridColumnKey.GetKey(cell.Column) ?? string.Empty,
                cell.RowIndex))
            .Where(cell => !string.IsNullOrWhiteSpace(cell.ColumnKey))
            .ToList();

        if (selectedCells.Count == 0
            && dataGrid.CurrentItem is SalaryRowVm currentRow
            && dataGrid.CurrentColumn is GridColumn currentColumn)
        {
            var currentColumnKey = DataGridColumnKey.GetKey(currentColumn);
            if (!string.IsNullOrWhiteSpace(currentColumnKey))
            {
                selectedCells.Add(new SalarySheetCellSelection(
                    currentRow,
                    currentColumnKey!,
                    dataGrid.SelectionController.CurrentCellManager.CurrentCell.RowIndex));
            }
        }

        return FromCellSelections(columns, selectedCells);
    }

    public static IReadOnlyList<SalarySheetColumnSelection> GetVisibleColumnSelections(SfDataGrid dataGrid)
        => dataGrid.Columns
            .Where(column => !column.IsHidden)
            .Select(ToColumnSelection)
            .Where(column => column is not null)
            .Cast<SalarySheetColumnSelection>()
            .ToList();

    public static bool HasCurrentGridCell(SfDataGrid dataGrid)
        => dataGrid.CurrentItem is SalaryRowVm
           && dataGrid.CurrentColumn is GridColumn column
           && !column.IsHidden
           && !string.IsNullOrWhiteSpace(column.MappingName)
           && !string.IsNullOrWhiteSpace(DataGridColumnKey.GetKey(column));

    public static SalarySheetSelectionSnapshot FromSingleCell(
        SfDataGrid dataGrid,
        SalaryRowVm row,
        GridColumn column,
        int rowOrder)
        => FromSingleCell(
            GetVisibleColumnSelections(dataGrid),
            row,
            DataGridColumnKey.GetKey(column),
            rowOrder);

    public static SalarySheetSelectionSnapshot FromSingleCell(
        IReadOnlyList<SalarySheetColumnSelection> visibleColumns,
        SalaryRowVm row,
        string? columnKey,
        int rowOrder)
    {
        if (string.IsNullOrWhiteSpace(columnKey))
            return EmptySnapshot();

        return FromCellSelections(
            visibleColumns,
            new[] { new SalarySheetCellSelection(row, columnKey, rowOrder) });
    }

    public static SalarySheetSelectionSnapshot FromCellSelections(
        IReadOnlyList<SalarySheetColumnSelection> visibleColumns,
        IEnumerable<SalarySheetCellSelection> selectedCells)
    {
        var visibleColumnKeys = visibleColumns
            .Select(column => column.Key)
            .ToHashSet(StringComparer.Ordinal);
        var cells = selectedCells
            .Where(cell => !string.IsNullOrWhiteSpace(cell.ColumnKey)
                           && visibleColumnKeys.Contains(cell.ColumnKey))
            .ToList();
        if (cells.Count == 0 || visibleColumns.Count == 0)
            return EmptySnapshot();

        var selectedColumnKeys = cells
            .Select(cell => cell.ColumnKey)
            .ToHashSet(StringComparer.Ordinal);
        var selectedRows = cells
            .GroupBy(cell => cell.Row)
            .OrderBy(group => group.Min(cell => cell.RowOrder))
            .Select(group => group.Key)
            .ToList();
        var selectedColumns = visibleColumns
            .Where(column => selectedColumnKeys.Contains(column.Key))
            .ToList();

        return new SalarySheetSelectionSnapshot(selectedRows, selectedColumns);
    }

    private static SalarySheetSelectionSnapshot EmptySnapshot()
        => new(Array.Empty<SalaryRowVm>(), Array.Empty<SalarySheetColumnSelection>());

    private static SalarySheetColumnSelection? ToColumnSelection(GridColumn column)
    {
        var key = DataGridColumnKey.GetKey(column);
        var mappingName = column.MappingName;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(mappingName))
            return null;

        var header = string.IsNullOrWhiteSpace(column.HeaderText) ? mappingName : column.HeaderText;
        return new SalarySheetColumnSelection(key, mappingName, header);
    }
}
