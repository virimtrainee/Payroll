using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Views;

public partial class SalarySheetView : UserControl
{
    private bool _initialized;

    public SalarySheetView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        if (DataContext is not SalarySheetViewModel vm) return;
        _initialized = true;
        vm.LoadCommand.Execute(null);
    }

    // Ctrl+S saves from anywhere inside the view, including while a cell is being edited
    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
        {
            if (DataContext is SalarySheetViewModel vm)
            {
                SalaryGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
                vm.SaveAttendanceCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    // Auto-enter edit mode when a cell becomes current (only in edit mode)
    private void SalaryGrid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (sender is not DataGrid dg || dg.IsReadOnly) return;
        Application.Current.Dispatcher.InvokeAsync(
            () => BeginEditAndFocus(dg),
            DispatcherPriority.Input);
    }

    // Enter moves focus through editable columns left-to-right, then wraps to next row
    private void SalaryGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is not DataGrid dg || dg.IsReadOnly) return;

        var editableHeaders = new[] { "Absent", "ESIC", "PF", "TDS", "Adv. Ded" };
        var editableCols = dg.Columns
            .Where(c => editableHeaders.Contains(c.Header?.ToString()))
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        if (editableCols.Count == 0) return;

        dg.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);

        var curRowIdx = Math.Max(0, dg.Items.IndexOf(dg.CurrentItem));
        var colIdx = Math.Max(-1, editableCols.IndexOf(dg.CurrentColumn));
        var (nextRowIdx, nextCol) = FindNextEditableCell(dg, editableCols, curRowIdx, colIdx);

        e.Handled = true;

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            dg.CurrentCell = new DataGridCellInfo(dg.Items[nextRowIdx], nextCol);
            dg.ScrollIntoView(dg.Items[nextRowIdx], nextCol);
            BeginEditAndFocus(dg);
        }, DispatcherPriority.Input);
    }

    private static void BeginEditAndFocus(DataGrid dg)
    {
        if (!IsEditableCell(dg.CurrentCell.Item, dg.CurrentCell.Column)) return;

        dg.BeginEdit();

        // Find the TextBox inside the editing template and focus it
        if (GetCurrentCell(dg) is { } cell)
        {
            var tb = FindChild<TextBox>(cell);
            if (tb != null)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }
    }

    private static (int RowIndex, DataGridColumn Column) FindNextEditableCell(
        DataGrid dg,
        System.Collections.Generic.IReadOnlyList<DataGridColumn> editableCols,
        int rowIndex,
        int colIndex)
    {
        var itemCount = dg.Items.Count;
        for (var step = 1; step <= itemCount * editableCols.Count; step++)
        {
            var flat = rowIndex * editableCols.Count + colIndex + step;
            var nextRow = (flat / editableCols.Count) % itemCount;
            var nextCol = editableCols[flat % editableCols.Count];
            var item = dg.Items[nextRow];
            if (IsEditableCell(item, nextCol))
                return (nextRow, nextCol);
        }

        return (rowIndex, editableCols[0]);
    }

    private static bool IsEditableCell(object? item, DataGridColumn? column)
    {
        if (item is not SalaryRowVm row || column is null) return false;
        return column.Header?.ToString() switch
        {
            "Absent" => true,
            "ESIC" => row.UsesEsicPf,
            "PF" => row.UsesEsicPf,
            "TDS" => row.UsesTds,
            "Adv. Ded" => true,
            _ => false
        };
    }

    private static DataGridCell? GetCurrentCell(DataGrid dg)
    {
        if (dg.CurrentCell.Item is null) return null;
        if (dg.ItemContainerGenerator.ContainerFromItem(dg.CurrentCell.Item)
            is not DataGridRow row) return null;

        var presenter = FindChild<DataGridCellsPresenter>(row);
        if (presenter is null) return null;

        for (int i = 0; i < dg.Columns.Count; i++)
        {
            if (presenter.ItemContainerGenerator.ContainerFromIndex(i) is DataGridCell cell
                && cell.Column == dg.CurrentCell.Column)
                return cell;
        }
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
}
