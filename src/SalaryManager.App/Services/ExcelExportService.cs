using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ClosedXML.Report;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.App.Services;

public record IciciPaymentRow(string Name, string? AccountNumber, string? IfscCode, decimal Amount, PaymentMode PaymentMode);

public record IciciExportOptions(string DebitAccountNo, DateTime PaymentDate);

public class ExcelExportService
{
    public string ExportMonthlySummary(int year, int month, IReadOnlyList<MonthlySummaryRow> rows, string path)
    {
        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        using var template = new XLTemplate(ExcelReportTemplateFactory.CreateMonthlySummaryTemplate($"{monthName} {year}"));
        template.AddVariable("MonthName", monthName);
        template.AddVariable("Year", year);
        template.AddVariable("Rows", rows);
        template.Generate();

        var ws = template.Workbook.Worksheet(1);
        ApplyMonthlySummaryTotals(ws, rows);
        ApplyMonthlySummaryLayout(ws);
        SaveReport(template, path);
        return path;
    }

    public string ExportIciciPayment(IReadOnlyList<IciciPaymentRow> rows, IciciExportOptions options, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        // Header row
        var headers = new[] { "PYMT_PROD_TYPE_CODE", "PYMT_MODE", "DEBIT_ACC_NO",
                               "BNF_NAME", "BENE_ACC_NO", "BENE_IFSC",
                               "AMOUNT", "PYMT_DATE", "REMARK" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        }

        var payDate = options.PaymentDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        int r = 2;
        foreach (var row in rows)
        {
            var mode = row.PaymentMode == PaymentMode.IciciBank ? "FT" : "NEFT";
            ws.Cell(r, 1).Value = "PAB_VENDOR";
            ws.Cell(r, 2).Value = mode;
            ws.Cell(r, 3).Value = options.DebitAccountNo;
            ws.Cell(r, 4).Value = row.Name;
            ws.Cell(r, 5).Value = row.AccountNumber ?? "";
            ws.Cell(r, 6).Value = row.IfscCode ?? "";
            ws.Cell(r, 7).Value = row.Amount;
            ws.Cell(r, 7).Style.NumberFormat.Format = "0.00";
            ws.Cell(r, 8).Value = payDate;
            ws.Cell(r, 9).Value = "";
            r++;
        }

        ApplyIciciPaymentLayout(ws);
        EnsureDirectory(path);
        wb.SaveAs(path);
        return path;
    }

    public string ExportAdvanceLedger(Employee emp, IReadOnlyList<LedgerRow> rows, decimal balance, string path)
    {
        var data = rows.Select(row => new AdvanceLedgerExportRow(
            row.Entry.Date,
            row.Entry.EntryType.ToString(),
            row.Entry.Amount,
            row.Entry.Note ?? "",
            row.RunningBalance)).ToList();

        using var template = new XLTemplate(ExcelReportTemplateFactory.CreateAdvanceLedgerTemplate());
        template.AddVariable("EmployeeName", emp.Name);
        template.AddVariable("BalanceText", balance.ToString("N2", CultureInfo.CurrentCulture));
        template.AddVariable("Rows", data);
        template.Generate();
        ApplyAdvanceLedgerLayout(template.Workbook.Worksheet("Advance Ledger"));
        SaveReport(template, path);
        return path;
    }

    public string ExportSalaryRevisions(IReadOnlyList<SalaryRevisionReportRow> rows, string path)
    {
        using var template = new XLTemplate(ExcelReportTemplateFactory.CreateSalaryRevisionsTemplate());
        template.AddVariable("Rows", rows);
        template.Generate();
        ApplySalaryRevisionsLayout(template.Workbook.Worksheet("Salary Revisions"));
        SaveReport(template, path);
        return path;
    }

    private static void SaveReport(XLTemplate template, string path)
    {
        EnsureDirectory(path);
        template.SaveAs(path);
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static void ApplyMonthlySummaryTotals(IXLWorksheet ws, IReadOnlyList<MonthlySummaryRow> rows)
    {
        var totalRow = 4 + rows.Count;
        ws.Cell(totalRow, 1).Value = "TOTAL";
        ws.Cell(totalRow, 2).Value = rows.Sum(row => row.BaseSalary);
        ws.Cell(totalRow, 4).Value = rows.Sum(row => row.Deduction);
        ws.Cell(totalRow, 5).Value = rows.Sum(row => row.SalaryPaid);
        ws.Cell(totalRow, 6).Value = rows.Sum(row => row.EsicDeduction);
        ws.Cell(totalRow, 7).Value = rows.Sum(row => row.PfDeduction);
        ws.Cell(totalRow, 8).Value = rows.Sum(row => row.TdsDeduction);
        ws.Cell(totalRow, 9).Value = rows.Sum(row => row.AdvanceDeduction);
        ws.Cell(totalRow, 10).Value = rows.Sum(row => row.NetSalary);
    }

    private static void ApplyMonthlySummaryLayout(IXLWorksheet ws)
        => SetColumnWidths(ws, 28, 14, 12, 14, 14, 12, 12, 12, 18, 14);

    private static void ApplyIciciPaymentLayout(IXLWorksheet ws)
        => SetColumnWidths(ws, 22, 12, 18, 28, 20, 16, 14, 14, 20);

    private static void ApplyAdvanceLedgerLayout(IXLWorksheet ws)
        => SetColumnWidths(ws, 13, 12, 14, 32, 14);

    private static void ApplySalaryRevisionsLayout(IXLWorksheet ws)
        => SetColumnWidths(ws, 28, 14, 14, 14, 32);

    private static void SetColumnWidths(IXLWorksheet ws, params double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
            ws.Column(i + 1).Width = widths[i];
    }

    private sealed record AdvanceLedgerExportRow(
        DateTime Date,
        string Type,
        decimal Amount,
        string Note,
        decimal Balance);
}
