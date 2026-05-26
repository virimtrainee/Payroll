using ClosedXML.Excel;
using SalaryManager.App.Services;
using SalaryManager.Data.Validation;
using Xunit;

namespace SalaryManager.Tests;

public class ExcelImportServiceTests
{
    [Fact]
    public void ReadEmployees_RequiresSalaryHeader()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Employees");
        ws.Cell(1, 1).Value = "NAME";
        ws.Cell(2, 1).Value = "A";

        var result = Read(workbook);

        Assert.NotNull(result.Error);
        Assert.Contains("salary column", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadEmployees_ParsesNumericSalaryCell()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Employees");
        ws.Cell(1, 1).Value = "NAME";
        ws.Cell(1, 2).Value = "SALARY";
        ws.Cell(2, 1).Value = "A";
        ws.Cell(2, 2).Value = 1234.50m;

        var result = Read(workbook);

        Assert.Null(result.Error);
        Assert.Empty(result.Issues);
        var row = Assert.Single(result.Rows);
        Assert.Equal(1234.50m, row.BaseSalary);
    }

    [Fact]
    public void ReadEmployees_ReturnsRowErrorForInvalidSalary()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Employees");
        ws.Cell(1, 1).Value = "NAME";
        ws.Cell(1, 2).Value = "SALARY";
        ws.Cell(2, 1).Value = "A";
        ws.Cell(2, 2).Value = "not a number";

        var result = Read(workbook);

        Assert.Null(result.Error);
        var issue = Assert.Single(result.Issues, issue => issue.Code == "salary_invalid");
        Assert.Equal(2, issue.RowNumber);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ReadEmployees_ReturnsWorkbookErrorForCorruptFile()
    {
        var path = TempPath();
        File.WriteAllText(path, "not an xlsx file");

        try
        {
            var result = new ExcelImportService().ReadEmployees(path);

            Assert.NotNull(result.Error);
            Assert.Contains("Could not read", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadEmployees_InfersIciciModeFromIfscWhenModeBlank()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Employees");
        ws.Cell(1, 1).Value = "NAME";
        ws.Cell(1, 2).Value = "SALARY";
        ws.Cell(1, 3).Value = "BENE_ACC_NO";
        ws.Cell(1, 4).Value = "BENE_IFSC";
        ws.Cell(2, 1).Value = "A";
        ws.Cell(2, 2).Value = 1234.50m;
        ws.Cell(2, 3).Value = "123456789";
        ws.Cell(2, 4).Value = "ICIC0123456";

        var result = Read(workbook);

        var row = Assert.Single(result.Rows);
        Assert.Equal(SalaryManager.Data.Entities.PaymentMode.IciciBank, row.PaymentMode);
    }

    [Fact]
    public void ReadEmployees_ReturnsRowErrorForDuplicateWorkbookName()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Employees");
        ws.Cell(1, 1).Value = "NAME";
        ws.Cell(1, 2).Value = "SALARY";
        ws.Cell(2, 1).Value = "A";
        ws.Cell(2, 2).Value = 1000m;
        ws.Cell(3, 1).Value = "a";
        ws.Cell(3, 2).Value = 1200m;

        var result = Read(workbook);

        Assert.Contains(result.Issues, issue => issue.Code == "name_duplicate_import");
    }

    private static EmployeeImportReadResult Read(XLWorkbook workbook)
    {
        var path = TempPath();
        try
        {
            workbook.SaveAs(path);
            return new ExcelImportService().ReadEmployees(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
}
