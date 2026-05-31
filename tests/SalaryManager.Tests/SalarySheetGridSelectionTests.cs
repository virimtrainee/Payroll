using SalaryManager.App.Helpers;
using SalaryManager.App.ViewModels;

namespace SalaryManager.Tests;

public class SalarySheetGridSelectionTests
{
    [Fact]
    public void FromSingleCell_CreatesSnapshotForKnownVisibleColumn()
    {
        var row = new SalaryRowVm { Name = "A", EmployeeBaseSalary = 10000m };
        var columns = new[]
        {
            new SalarySheetColumnSelection("employee", "Name", "Employee"),
            new SalarySheetColumnSelection("salaryPaid", "SalaryPaid", "Salary Paid"),
            new SalarySheetColumnSelection("tds", "TdsDeduction", "TDS")
        };

        var snapshot = SalarySheetGridSelection.FromSingleCell(columns, row, "salaryPaid", 7);

        Assert.True(snapshot.HasSelection);
        Assert.Same(row, Assert.Single(snapshot.Rows));
        Assert.Equal(["salaryPaid"], snapshot.ColumnKeys);
        Assert.Equal(["SalaryPaid"], snapshot.MappingNames);
        Assert.True(snapshot.Contains(row, "SalaryPaid"));
    }

    [Fact]
    public void FromSingleCell_UnknownColumnKeyProducesNoActionableSnapshot()
    {
        var row = new SalaryRowVm { Name = "A", EmployeeBaseSalary = 10000m };
        var columns = new[]
        {
            new SalarySheetColumnSelection("employee", "Name", "Employee"),
            new SalarySheetColumnSelection("salaryPaid", "SalaryPaid", "Salary Paid")
        };

        var snapshot = SalarySheetGridSelection.FromSingleCell(columns, row, "missing", 7);

        Assert.False(snapshot.HasSelection);
        Assert.Empty(snapshot.Rows);
        Assert.Empty(snapshot.Columns);
    }

    [Fact]
    public void FromCellSelections_OrdersSelectedRowsAndVisibleColumns()
    {
        var rowA = new SalaryRowVm { Name = "A", EmployeeBaseSalary = 10000m };
        var rowB = new SalaryRowVm { Name = "B", EmployeeBaseSalary = 10000m };
        var rowC = new SalaryRowVm { Name = "C", EmployeeBaseSalary = 10000m };
        var columns = new[]
        {
            new SalarySheetColumnSelection("employee", "Name", "Employee"),
            new SalarySheetColumnSelection("absent", "DaysAbsent", "Absent"),
            new SalarySheetColumnSelection("salaryPaid", "SalaryPaid", "Salary Paid"),
            new SalarySheetColumnSelection("tds", "TdsDeduction", "TDS")
        };
        var cells = new[]
        {
            new SalarySheetCellSelection(rowC, "tds", 3),
            new SalarySheetCellSelection(rowA, "salaryPaid", 1),
            new SalarySheetCellSelection(rowC, "absent", 3)
        };

        var snapshot = SalarySheetGridSelection.FromCellSelections(columns, cells);

        Assert.Equal(["A", "C"], snapshot.Rows.Select(row => row.Name));
        Assert.Equal(["absent", "salaryPaid", "tds"], snapshot.ColumnKeys);
        Assert.Equal(["DaysAbsent", "SalaryPaid", "TdsDeduction"], snapshot.MappingNames);
        Assert.True(snapshot.Contains(rowC, "TdsDeduction"));
        Assert.False(snapshot.Contains(rowB, "TdsDeduction"));
    }
}
