using QuestPDF.Infrastructure;
using SalaryManager.App.Services;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class PdfSlipServiceTests
{
    public PdfSlipServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void GenerateMonthlySummary_WritesPdfForFilenameOnlyPath()
    {
        var path = $"{Guid.NewGuid():N}.pdf";
        try
        {
            var rows = new[]
            {
                new MonthlySummaryRow("A", 10000m, 10000m, 0, 0m, 0m, 0m, 0m, 0m, 10000m),
                new MonthlySummaryRow("B", 30000m, 29032.26m, 1, 967.74m, 0m, 0m, 100m, 50m, 28882.26m),
            };

            new PdfSlipService().GenerateMonthlySummary(2026, 5, rows, path);

            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void GenerateSlip_WritesPdfWithNetSalaryOverride()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            var employee = new Employee { Id = 1, Name = "A", BaseSalary = 10000m };
            var breakdown = SalaryCalculator.Compute(10000m, 2026, 5, 0);
            var data = new SalarySlipData(employee, 2026, 5, breakdown, 0m, 0m, 8750m);

            new PdfSlipService().GenerateSlip(data, path);

            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void GenerateSalarySheetSelection_WritesPdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            var columns = new[]
            {
                new SalarySheetSelectionColumn("Employee", false),
                new SalarySheetSelectionColumn("Salary Paid", true),
                new SalarySheetSelectionColumn("Net", true)
            };
            var rows = new[]
            {
                new SalarySheetSelectionRow(["A", "10,000.00", "9,500.00"]),
                new SalarySheetSelectionRow(["B", "20,000.00", "18,750.00"])
            };

            new PdfSlipService().GenerateSalarySheetSelection(2026, 5, columns, rows, path);

            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
