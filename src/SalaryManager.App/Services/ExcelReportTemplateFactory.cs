using ClosedXML.Excel;

namespace SalaryManager.App.Services;

internal static class ExcelReportTemplateFactory
{
    public static XLWorkbook CreateMonthlySummaryTemplate(string worksheetName)
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(worksheetName);

        ws.Cell(1, 1).Value = "Payroll Summary - {{MonthName}} {{Year}}";
        ws.Range(1, 1, 1, 10).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        AddHeaders(ws, 3, new[]
        {
            "Employee", "Base Salary", "Days Absent", "Deduction", "Salary Paid", "PF", "ESIC", "TDS", "Advance Deduction", "Net Salary"
        });

        var expressions = new[]
        {
            "{{item.EmployeeName}}",
            "{{item.BaseSalary}}",
            "{{item.DaysAbsent}}",
            "{{item.Deduction}}",
            "{{item.SalaryPaid}}",
            "{{item.PfDeduction}}",
            "{{item.EsicDeduction}}",
            "{{item.TdsDeduction}}",
            "{{item.AdvanceDeduction}}",
            "{{item.NetSalary}}"
        };
        AddRow(ws, 4, expressions);

        ws.Cell(5, 1).Value = "TOTAL";
        foreach (var column in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            ws.Cell(5, column).Value = "<<sum>>";
        ws.Range(5, 1, 5, 10).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#2563EB"))
            .Font.SetFontColor(XLColor.White);

        ws.Range(4, 2, 5, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 4, 5, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 5, 5, 5).Style.NumberFormat.Format = "#,##0";
        ws.Range(4, 6, 5, 10).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 1, 5, 10).AddToNamed("Rows");

        return wb;
    }

    public static XLWorkbook CreateAdvanceLedgerTemplate()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Advance Ledger");

        ws.Cell(1, 1).Value = "Advance Ledger - {{EmployeeName}}";
        ws.Range(1, 1, 1, 5).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = "Outstanding balance: {{BalanceText}}";

        AddHeaders(ws, 4, new[] { "Date", "Type", "Amount", "Note", "Balance" });
        AddRow(ws, 5, new[]
        {
            "{{item.Date}}",
            "{{item.Type}}",
            "{{item.Amount}}",
            "{{item.Note}}",
            "{{item.Balance}}"
        });

        ws.Cell(6, 1).Value = "<<Range>>";
        ws.Range(5, 1, 6, 5).AddToNamed("Rows");
        ws.Range(5, 3, 6, 3).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(5, 5, 6, 5).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(5, 1, 6, 1).Style.DateFormat.Format = "yyyy-MM-dd";

        return wb;
    }

    public static XLWorkbook CreateSalaryRevisionsTemplate()
    {
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Salary Revisions");

        ws.Cell(1, 1).Value = "Salary Revisions Report";
        ws.Range(1, 1, 1, 5).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        AddHeaders(ws, 3, new[] { "Employee", "Old Salary", "New Salary", "Changed At", "Note" });
        AddRow(ws, 4, new[]
        {
            "{{item.EmployeeName}}",
            "{{item.OldSalary}}",
            "{{item.NewSalary}}",
            "{{item.ChangedAt}}",
            "{{item.Note}}"
        });

        ws.Cell(5, 1).Value = "<<Range>>";
        ws.Range(4, 1, 5, 5).AddToNamed("Rows");
        ws.Range(4, 2, 5, 3).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(4, 4, 5, 4).Style.DateFormat.Format = "yyyy-MM-dd";

        return wb;
    }

    private static void AddHeaders(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }
    }

    private static void AddRow(IXLWorksheet ws, int row, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
            ws.Cell(row, i + 1).Value = values[i];
    }
}
