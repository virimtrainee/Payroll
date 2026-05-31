using System;
using System.Collections.Generic;
using System.Linq;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Helpers;

public static class SalarySheetGridNavigation
{
    private static readonly string[] EditableColumnKeys =
        { "absent", "salaryPaid", "pf", "esic", "tds", "advanceDeduction" };

    public static IReadOnlyList<string> EditableColumnKeyOrder => EditableColumnKeys;

    public static IReadOnlyList<string> GetEditableColumnKeys(SalaryRowVm row)
        => EditableColumnKeyOrder
            .Where(key => IsEditableColumnKey(row, key))
            .ToArray();

    public static bool IsEditableColumnKey(SalaryRowVm row, string? columnKey)
        => columnKey switch
        {
            "salaryPaid" => true,
            "absent" => true,
            "esic" => row.UsesEsicPf,
            "pf" => row.UsesEsicPf,
            "tds" => row.UsesTds,
            "advanceDeduction" => true,
            _ => false
        };

    public static (int RowIndex, string ColumnKey) FindNextEditableColumnKey(
        IReadOnlyList<SalaryRowVm> rows,
        int rowIndex,
        string? currentColumnKey,
        IReadOnlySet<string>? availableColumnKeys = null)
    {
        if (rows.Count == 0)
            return (-1, string.Empty);

        var currentRowIndex = Math.Clamp(rowIndex, 0, rows.Count - 1);
        var currentColumnIndex = Array.IndexOf(EditableColumnKeys, currentColumnKey);
        var columnCount = EditableColumnKeys.Length;

        for (var step = 1; step <= rows.Count * columnCount; step++)
        {
            var flat = currentRowIndex * columnCount + currentColumnIndex + step;
            var nextRowIndex = (flat / columnCount) % rows.Count;
            var nextKey = EditableColumnKeys[flat % columnCount];
            if (availableColumnKeys is not null && !availableColumnKeys.Contains(nextKey))
                continue;
            if (IsEditableColumnKey(rows[nextRowIndex], nextKey))
                return (nextRowIndex, nextKey);
        }

        return (currentRowIndex, EditableColumnKeys[0]);
    }
}
