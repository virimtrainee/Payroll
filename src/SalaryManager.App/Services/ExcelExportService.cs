using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.App.Services;

public record IciciPaymentRow(string Name, string? AccountNumber, string? IfscCode, decimal Amount, PaymentMode PaymentMode);

public class ExcelExportService
{
    // Company ICICI debit account — update this to match your bank account number
    private const string DebitAccountNo = "777705679980";
    public string ExportMonthlySummary(int year, int month, IReadOnlyList<MonthlySummaryRow> rows, string path)
    {
        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet($"{monthName} {year}");

        ws.Cell(1, 1).Value = $"Payroll Summary — {monthName} {year}";
        ws.Range(1, 1, 1, 7).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        var headers = new[] { "Employee", "Base Salary", "Days Absent", "Deduction", "ESIC", "PF", "Net Salary" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(3, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }

        int r = 4;
        decimal tBase = 0, tDed = 0, tEsic = 0, tPf = 0, tNet = 0;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.EmployeeName;
            ws.Cell(r, 2).Value = row.BaseSalary;
            ws.Cell(r, 3).Value = row.DaysAbsent;
            ws.Cell(r, 4).Value = row.Deduction;
            ws.Cell(r, 5).Value = row.EsicDeduction;
            ws.Cell(r, 6).Value = row.PfDeduction;
            ws.Cell(r, 7).Value = row.NetSalary;

            tBase += row.BaseSalary;
            tDed  += row.Deduction;
            tEsic += row.EsicDeduction;
            tPf   += row.PfDeduction;
            tNet  += row.NetSalary;
            r++;
        }

        ws.Cell(r, 1).Value = "TOTAL";
        ws.Cell(r, 2).Value = tBase;
        ws.Cell(r, 4).Value = tDed;
        ws.Cell(r, 5).Value = tEsic;
        ws.Cell(r, 6).Value = tPf;
        ws.Cell(r, 7).Value = tNet;
        ws.Range(r, 1, r, 7).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#2563EB"))
            .Font.SetFontColor(XLColor.White);

        ws.Range(4, 2, r, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 4, r, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 5, r, 5).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 6, r, 6).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 7, r, 7).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        wb.SaveAs(path);
        return path;
    }

    public string ExportIciciPayment(IReadOnlyList<IciciPaymentRow> rows, string path)
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

        var payDate = DateTime.Today.ToString("dd-MM-yyyy");
        int r = 2;
        foreach (var row in rows)
        {
            var mode = row.PaymentMode == PaymentMode.IciciBank ? "FT" : "NEFT";
            ws.Cell(r, 1).Value = "PAB_VENDOR";
            ws.Cell(r, 2).Value = mode;
            ws.Cell(r, 3).Value = DebitAccountNo;
            ws.Cell(r, 4).Value = row.Name;
            ws.Cell(r, 5).Value = row.AccountNumber ?? "";
            ws.Cell(r, 6).Value = row.IfscCode ?? "";
            ws.Cell(r, 7).Value = row.Amount.ToString("F2", CultureInfo.InvariantCulture);
            ws.Cell(r, 8).Value = payDate;
            ws.Cell(r, 9).Value = "";
            r++;
        }

        ws.Columns().AdjustToContents();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        wb.SaveAs(path);
        return path;
    }

    public string ExportAdvanceLedger(Employee emp, IReadOnlyList<LedgerRow> rows, decimal balance, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Advance Ledger");

        ws.Cell(1, 1).Value = $"Advance Ledger — {emp.Name}";
        ws.Range(1, 1, 1, 5).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"Outstanding balance: {balance:N2}";

        var headers = new[] { "Date", "Type", "Amount", "Note", "Balance" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }

        int r = 5;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Entry.Date;
            ws.Cell(r, 1).Style.DateFormat.Format = "yyyy-MM-dd";
            ws.Cell(r, 2).Value = row.Entry.EntryType.ToString();
            ws.Cell(r, 3).Value = row.Entry.Amount;
            ws.Cell(r, 4).Value = row.Entry.Note ?? "";
            ws.Cell(r, 5).Value = row.RunningBalance;
            r++;
        }
        ws.Range(5, 3, r - 1, 3).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(5, 5, r - 1, 5).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        wb.SaveAs(path);
        return path;
    }
}
