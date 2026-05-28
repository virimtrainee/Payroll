using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SalaryManager.App.ViewModels;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace SalaryManager.App.Helpers;

public sealed class SalarySheetGridSelectionController : GridCellSelectionController
{
    private readonly SfDataGrid _dataGrid;

    public SalarySheetGridSelectionController(SfDataGrid dataGrid)
        : base(dataGrid)
    {
        _dataGrid = dataGrid;
    }

    protected override void ProcessKeyDown(KeyEventArgs args)
    {
        if (!ShouldHandleEnter(args))
        {
            base.ProcessKeyDown(args);
            return;
        }

        if (!MoveToNextSalaryCell())
        {
            base.ProcessKeyDown(args);
            return;
        }

        args.Handled = true;
    }

    private bool ShouldHandleEnter(KeyEventArgs args)
        => !_dataGrid.IsReadOnly
           && args.KeyboardDevice.Modifiers == ModifierKeys.None
           && (args.Key == Key.Enter || args.Key == Key.Return);

    private bool MoveToNextSalaryCell()
    {
        if (_dataGrid.CurrentItem is not SalaryRowVm)
            return false;

        var rows = GetVisibleSalaryRows();
        if (rows.Count == 0)
            return false;

        CurrentCellManager.EndEdit(true);

        var currentRowIndex = ResolveCurrentSalaryRowIndex(rows);
        var currentColumnKey = ResolveCurrentColumnKey();
        var availableColumnKeys = _dataGrid.Columns
            .Where(column => !column.IsHidden)
            .Select(DataGridColumnKey.GetKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
        var (nextRowIndex, nextColumnKey) = SalarySheetGridNavigation.FindNextEditableColumnKey(
            rows.Select(row => row.Row).ToList(),
            currentRowIndex,
            currentColumnKey,
            availableColumnKeys);
        if (nextRowIndex < 0)
            return false;

        var nextColumn = _dataGrid.Columns.FirstOrDefault(column =>
            !column.IsHidden && DataGridColumnKey.GetKey(column) == nextColumnKey);
        if (nextColumn is null)
            return false;

        var nextItem = rows[nextRowIndex].Row;
        var nextCell = new RowColumnIndex(
            rows[nextRowIndex].RowIndex,
            SelectionHelper.GetFirstColumnIndex(_dataGrid, FlowDirection.LeftToRight) + _dataGrid.Columns.IndexOf(nextColumn));

        _dataGrid.CurrentItem = nextItem;
        _dataGrid.CurrentColumn = nextColumn;
        _dataGrid.ScrollInView(nextCell);
        MoveCurrentCell(nextCell, needToClearSelection: true);

        _dataGrid.Dispatcher.BeginInvoke(() =>
        {
            if (_dataGrid.CurrentItem is SalaryRowVm row
                && SalarySheetGridNavigation.IsEditableColumnKey(row, DataGridColumnKey.GetKey(_dataGrid.CurrentColumn)))
            {
                CurrentCellManager.BeginEdit();
            }
        }, DispatcherPriority.Input);

        return true;
    }

    private int ResolveCurrentSalaryRowIndex(IReadOnlyList<(int RowIndex, SalaryRowVm Row)> rows)
    {
        if (_dataGrid.CurrentItem is SalaryRowVm currentRow)
        {
            var currentItemIndex = rows.ToList().FindIndex(row => ReferenceEquals(row.Row, currentRow));
            if (currentItemIndex >= 0)
                return currentItemIndex;
        }

        var currentCell = CurrentCellManager.CurrentCell;
        var currentCellIndex = rows.ToList().FindIndex(row => row.RowIndex == currentCell.RowIndex);
        return currentCellIndex >= 0 ? currentCellIndex : 0;
    }

    private string? ResolveCurrentColumnKey()
    {
        if (_dataGrid.CurrentColumn is not null)
            return DataGridColumnKey.GetKey(_dataGrid.CurrentColumn);

        var currentCell = CurrentCellManager.CurrentCell;
        var columnIndex = _dataGrid.ResolveToGridVisibleColumnIndex(currentCell.ColumnIndex);
        return columnIndex >= 0 && columnIndex < _dataGrid.Columns.Count
            ? DataGridColumnKey.GetKey(_dataGrid.Columns[columnIndex])
            : null;
    }

    private List<(int RowIndex, SalaryRowVm Row)> GetVisibleSalaryRows()
    {
        var rows = new List<(int RowIndex, SalaryRowVm Row)>();
        var firstDataRowIndex = SelectionHelper.GetFirstDataRowIndex(_dataGrid);
        var lastDataRowIndex = SelectionHelper.GetLastDataRowIndex(_dataGrid);
        if (firstDataRowIndex < 0 || lastDataRowIndex < firstDataRowIndex)
            return rows;

        for (var rowIndex = firstDataRowIndex; rowIndex <= lastDataRowIndex; rowIndex++)
        {
            if (SelectionHelper.GetRecordAtRowIndex(_dataGrid, rowIndex) is SalaryRowVm row)
                rows.Add((rowIndex, row));
        }

        return rows;
    }
}
