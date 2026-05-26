using ClosedXML.Excel;
using SalaryManager.App.Services;
using SalaryManager.Data.Entities;
using Xunit;

namespace SalaryManager.Tests;

public class ExcelExportServiceTests
{
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
