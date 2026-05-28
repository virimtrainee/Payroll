using ClosedXML.Excel;
using SalaryManager.App.Services;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class ExcelExportServiceTests
{
    [Fact]
    public void ExportMonthlySummary_ExpandsRowsAndTotals()
    {
        var path = TempPath();
        try
        {
            var rows = new[]
            {
                new MonthlySummaryRow("A", 10000m, 0, 0m, 10m, 20m, 30m, 40m, 9900m),
                new MonthlySummaryRow("B", 20000m, 2, 500m, 15m, 25m, 35m, 45m, 19380m),
            };

            new ExcelExportService().ExportMonthlySummary(2026, 5, rows, path);

            using var workbook = new XLWorkbook(path);
            var ws = workbook.Worksheet("May 2026");
            Assert.Equal("Payroll Summary - May 2026", ws.Cell(1, 1).GetString());
            Assert.Equal("Employee", ws.Cell(3, 1).GetString());
            Assert.Equal("A", ws.Cell(4, 1).GetString());
            Assert.Equal(10000m, ws.Cell(4, 2).GetValue<decimal>());
            Assert.Equal("B", ws.Cell(5, 1).GetString());
            Assert.Equal("TOTAL", ws.Cell(6, 1).GetString());
            Assert.Equal(30000m, ws.Cell(6, 2).GetValue<decimal>());
            Assert.Equal(500m, ws.Cell(6, 4).GetValue<decimal>());
            Assert.Equal(29280m, ws.Cell(6, 9).GetValue<decimal>());
            Assert.Equal(28d, ws.Column(1).Width, 2);
            Assert.Equal(18d, ws.Column(8).Width, 2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ExportIciciPayment_UsesSettingsAndNumericAmount()
    {
        var path = TempPath();
        try
        {
            var rows = new[]
            {
                new IciciPaymentRow("A", "123456789", "ICIC0123456", 1234.5m, PaymentMode.IciciBank),
                new IciciPaymentRow("B", "987654321", "HDFC0123456", 99m, PaymentMode.OtherBank),
            };
            var options = new IciciExportOptions("111122223333", new DateTime(2026, 5, 26));

            new ExcelExportService().ExportIciciPayment(rows, options, path);

            using var workbook = new XLWorkbook(path);
            var ws = workbook.Worksheet(1);
            Assert.Equal("111122223333", ws.Cell(2, 3).GetString());
            Assert.Equal("26-05-2026", ws.Cell(2, 8).GetString());
            Assert.Equal("FT", ws.Cell(2, 2).GetString());
            Assert.Equal("NEFT", ws.Cell(3, 2).GetString());
            Assert.Equal(1234.5m, ws.Cell(2, 7).GetValue<decimal>());
            Assert.Equal(22d, ws.Column(1).Width, 2);
            Assert.Equal(28d, ws.Column(4).Width, 2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ExportAdvanceLedger_ExpandsRows()
    {
        var path = TempPath();
        try
        {
            var employee = new Employee { Name = "Employee A" };
            var rows = new[]
            {
                new LedgerRow(new Advance
                {
                    Date = new DateTime(2026, 5, 1),
                    EntryType = AdvanceEntryType.Given,
                    Amount = 1000m,
                    Note = "Cash"
                }, 1000m),
                new LedgerRow(new Advance
                {
                    Date = new DateTime(2026, 5, 15),
                    EntryType = AdvanceEntryType.Deducted,
                    Amount = 250m
                }, 750m),
            };

            new ExcelExportService().ExportAdvanceLedger(employee, rows, 750m, path);

            using var workbook = new XLWorkbook(path);
            var ws = workbook.Worksheet("Advance Ledger");
            Assert.Equal("Advance Ledger - Employee A", ws.Cell(1, 1).GetString());
            Assert.Equal("Outstanding balance: 750.00", ws.Cell(2, 1).GetString());
            Assert.Equal(new DateTime(2026, 5, 1), ws.Cell(5, 1).GetValue<DateTime>());
            Assert.Equal("Given", ws.Cell(5, 2).GetString());
            Assert.Equal(1000m, ws.Cell(5, 3).GetValue<decimal>());
            Assert.Equal("Deducted", ws.Cell(6, 2).GetString());
            Assert.Equal(750m, ws.Cell(6, 5).GetValue<decimal>());
            Assert.Equal(32d, ws.Column(4).Width, 2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ExportSalaryRevisions_ExpandsRows()
    {
        var path = TempPath();
        try
        {
            var rows = new[]
            {
                new SalaryRevisionReportRow("A", 10000m, 12000m, new DateTime(2026, 5, 20), "Annual"),
            };

            new ExcelExportService().ExportSalaryRevisions(rows, path);

            using var workbook = new XLWorkbook(path);
            var ws = workbook.Worksheet("Salary Revisions");
            Assert.Equal("Salary Revisions Report", ws.Cell(1, 1).GetString());
            Assert.Equal("A", ws.Cell(4, 1).GetString());
            Assert.Equal(10000m, ws.Cell(4, 2).GetValue<decimal>());
            Assert.Equal(12000m, ws.Cell(4, 3).GetValue<decimal>());
            Assert.Equal(new DateTime(2026, 5, 20), ws.Cell(4, 4).GetValue<DateTime>());
            Assert.Equal("Annual", ws.Cell(4, 5).GetString());
            Assert.Equal(32d, ws.Column(5).Width, 2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
}
