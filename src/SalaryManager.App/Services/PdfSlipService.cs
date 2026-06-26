using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.App.Services;

public record SalarySlipData(
    Employee Employee,
    int Year,
    int Month,
    SalaryBreakdown Breakdown,
    decimal AdvanceBalance,
    decimal SalaryAdvanceDeduction,
    decimal? NetSalaryOverride = null,
    string? FirmName = null);

public record MonthlySummaryRow(
    string EmployeeName,
    decimal BaseSalary,
    decimal SalaryPaid,
    int DaysAbsent,
    decimal Deduction,
    decimal EsicDeduction,
    decimal PfDeduction,
    decimal TdsDeduction,
    decimal AdvanceDeduction,
    decimal NetSalary,
    string GroupName = "");

public record SalaryRevisionReportRow(
    string EmployeeName,
    decimal OldSalary,
    decimal NewSalary,
    DateTime ChangedAt,
    string? Note);

public record SalarySheetSelectionColumn(string Header, bool AlignRight, bool IncludeInTotal = false, string Key = "");

public record SalarySheetSelectionRow(IReadOnlyList<string> Values, string GroupName = "");

public class PdfSlipService
{
    private const string TableBorderColor = "#CBD5E1";

    public string GenerateSlip(SalarySlipData data, string? outputPath = null)
    {
        outputPath ??= DefaultSlipPath(data);
        EnsureDirectory(outputPath);
        BuildSlipDocument(data).GeneratePdf(outputPath);
        return outputPath;
    }

    public string GenerateMonthlySummary(int year, int month, IReadOnlyList<MonthlySummaryRow> rows, string outputPath)
    {
        EnsureDirectory(outputPath);
        BuildSummaryDocument(year, month, rows).GeneratePdf(outputPath);
        return outputPath;
    }

    public string GenerateAdvanceLedger(Employee emp, IReadOnlyList<LedgerRow> rows, decimal balance, string outputPath)
    {
        EnsureDirectory(outputPath);
        BuildLedgerDocument(emp, rows, balance).GeneratePdf(outputPath);
        return outputPath;
    }

    public string GenerateSalaryRevisionReport(IReadOnlyList<SalaryRevisionReportRow> rows, string outputPath)
    {
        EnsureDirectory(outputPath);
        BuildSalaryRevisionDocument(rows).GeneratePdf(outputPath);
        return outputPath;
    }

    public string GenerateSalarySheetSelection(
        int year,
        int month,
        IReadOnlyList<SalarySheetSelectionColumn> columns,
        IReadOnlyList<SalarySheetSelectionRow> rows,
        string outputPath)
    {
        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));

        if (rows.Count == 0)
            throw new ArgumentException("At least one row is required.", nameof(rows));

        EnsureDirectory(outputPath);
        BuildSalarySheetSelectionDocument(year, month, columns, rows).GeneratePdf(outputPath);
        return outputPath;
    }

    private static void EnsureDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static string DefaultSlipPath(SalarySlipData d)
    {
        var safeName = string.Join("_", d.Employee.Name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(AppPaths.SlipsDirectory,
            $"{safeName}-{d.Year:0000}-{d.Month:00}.pdf");
    }

    private static IDocument BuildSlipDocument(SalarySlipData d) => Document.Create(c =>
    {
        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(d.Month);
        var firmName = NormalizeSlipText(d.FirmName);
        c.Page(p =>
        {
            p.Size(PageSizes.A5);
            p.Margin(28);
            p.PageColor(Colors.White);
            p.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(10).FontColor("#0F172A"));

            p.Header().Column(col =>
            {
                if (firmName is not null)
                    col.Item().Text(firmName).FontSize(14).Bold().FontColor("#0F172A");
                col.Item().Text("SALARY SLIP").FontSize(20).Bold().FontColor("#2563EB");
                col.Item().Text($"{monthName} {d.Year}").FontSize(11).FontColor("#64748B");
                col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#E2E8F0");
            });

            p.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(10);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c2 =>
                    {
                        c2.Item().Text("EMPLOYEE").FontSize(8).FontColor("#64748B").Bold();
                        c2.Item().Text(d.Employee.Name).FontSize(14).Bold();
                    });
                });

                col.Item().PaddingTop(6).Background("#F8FAFC").Padding(12).Column(box =>
                {
                    box.Spacing(6);
                    DetailRow(box, "Base salary", d.Breakdown.BaseSalary.ToString("N2"));
                    DetailRow(box, "Days in month", d.Breakdown.DaysInMonth.ToString());
                    DetailRow(box, "Days absent", d.Breakdown.DaysAbsent.ToString());
                    DetailRow(box, "Days present", d.Breakdown.DaysPresent.ToString());
                    DetailRow(box, "Per-day rate", d.Breakdown.PerDayRate.ToString("N2"));
                    DetailRow(box, "Absence deduction", $"- {d.Breakdown.Deduction:N2}");
                    DetailRow(box, "Salary paid", d.Breakdown.SalaryPaid.ToString("N0"));
                    if (d.Breakdown.PfDeduction > 0)
                        DetailRow(box, "PF deduction", $"- {d.Breakdown.PfDeduction:N2}");
                    if (d.Breakdown.EsicDeduction > 0)
                        DetailRow(box, "ESIC deduction", $"- {d.Breakdown.EsicDeduction:N2}");
                    if (d.Breakdown.TdsDeduction > 0)
                        DetailRow(box, "TDS deduction", $"- {d.Breakdown.TdsDeduction:N2}");
                    if (d.SalaryAdvanceDeduction > 0)
                        DetailRow(box, "Advance deduction", $"- {d.SalaryAdvanceDeduction:N2}");
                    if (d.NetSalaryOverride.HasValue)
                        DetailRow(box, "Net salary override", d.NetSalaryOverride.Value.ToString("N2"));
                });

                col.Item().PaddingTop(8).Background("#2563EB").Padding(12).Row(r =>
                {
                    var payable = d.NetSalaryOverride ?? d.Breakdown.NetSalary - Math.Max(0, d.SalaryAdvanceDeduction);
                    r.RelativeItem().Text("PAYABLE NET").FontSize(11).Bold().FontColor(Colors.White);
                    r.ConstantItem(140).AlignRight().Text($"₹ {payable:N2}")
                        .FontSize(16).Bold().FontColor(Colors.White);
                });

                col.Item().PaddingTop(8).Text(t =>
                {
                    t.Span("Outstanding advance balance: ").FontColor("#64748B");
                    t.Span($"₹ {d.AdvanceBalance:N2}").Bold();
                    t.Span("  (tracked in the advance ledger).").FontColor("#64748B");
                });
            });

            p.Footer().Column(col =>
            {
                col.Item().PaddingTop(20).LineHorizontal(0.4f).LineColor("#E2E8F0");
                col.Item().PaddingTop(10).Row(r =>
                {
                    r.RelativeItem().Column(c2 =>
                    {
                        c2.Item().Text("FIRM").FontSize(8).FontColor("#64748B").Bold();
                        c2.Item().Text(firmName ?? string.Empty).FontSize(10).Bold();
                    });
                    r.RelativeItem().Column(c2 =>
                    {
                        c2.Item().AlignRight().Text("EMPLOYEE").FontSize(8).FontColor("#64748B").Bold();
                        c2.Item().AlignRight().Text(d.Employee.Name).FontSize(10).Bold();
                    });
                });
            });
        });
    });

    private static string? NormalizeSlipText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static void DetailRow(QuestPDF.Fluent.ColumnDescriptor parent, string label, string value)
    {
        parent.Item().Row(r =>
        {
            r.RelativeItem().Text(label).FontColor("#64748B");
            r.ConstantItem(120).AlignRight().Text(value).Bold();
        });
    }

    private static IDocument BuildSummaryDocument(int year, int month, IReadOnlyList<MonthlySummaryRow> rows) => Document.Create(c =>
    {
        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        c.Page(p =>
        {
            p.Size(PageSizes.A4.Landscape());
            p.Margin(30);
            p.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(10).FontColor("#0F172A"));

            p.Header().Column(col =>
            {
                col.Item().Text("MONTHLY PAYROLL SUMMARY").FontSize(18).Bold().FontColor("#2563EB");
                col.Item().Text($"{monthName} {year}").FontSize(11).FontColor("#64748B");
                col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#E2E8F0");
            });

            p.Content().PaddingVertical(12).Table(t =>
            {
                var groupedRows = rows
                    .GroupBy(r => PdfGroupName(r.GroupName))
                    .ToList();
                var showGroupTotals = rows.Any(r => !string.IsNullOrWhiteSpace(r.GroupName));

                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(3);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(2);
                });

                t.Header(h =>
                {
                    SummaryHeaderCell(h.Cell()).Text("Employee").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("Base Salary").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("Absent").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("Deduction").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("Salary Paid").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("PF").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("ESIC").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("TDS").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("Adv. Ded").Bold();
                    SummaryHeaderCell(h.Cell()).AlignRight().Text("Net Salary").Bold();
                });

                foreach (var group in groupedRows)
                {
                    if (showGroupTotals)
                    {
                        PdfTableCell(t.Cell().ColumnSpan(10u), 6, "#E0F2FE")
                            .Text(group.Key).Bold().FontColor("#0F172A");
                    }

                    foreach (var r in group)
                    {
                        PdfTableCell(t.Cell(), 6).Text(r.EmployeeName);
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.BaseSalary.ToString("N2"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.DaysAbsent.ToString(CultureInfo.CurrentCulture));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.Deduction.ToString("N2"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.SalaryPaid.ToString("N0"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.PfDeduction.ToString("N2"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.EsicDeduction.ToString("N2"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.TdsDeduction.ToString("N2"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.AdvanceDeduction.ToString("N2"));
                        PdfTableCell(t.Cell(), 6).AlignRight().Text(r.NetSalary.ToString("N2")).Bold();
                    }

                    if (showGroupTotals)
                        SummaryTotalRow(t, "Group Total", group, "#F8FAFC", "#0F172A");
                }

                SummaryTotalRow(t, showGroupTotals ? "Final Total" : "TOTAL", rows, "#2563EB", "#FFFFFF");
            });

            p.Footer().AlignRight().Text(x =>
            {
                x.Span("Page ").FontSize(9).FontColor("#64748B");
                x.CurrentPageNumber().FontSize(9).FontColor("#64748B");
                x.Span(" / ").FontSize(9).FontColor("#64748B");
                x.TotalPages().FontSize(9).FontColor("#64748B");
            });
        });
    });

    private static IDocument BuildSalarySheetSelectionDocument(
        int year,
        int month,
        IReadOnlyList<SalarySheetSelectionColumn> columns,
        IReadOnlyList<SalarySheetSelectionRow> rows) => Document.Create(c =>
    {
        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        var compact = columns.Count > 10;
        var cellPadding = compact ? 3 : 5;
        var fontSize = compact ? 8 : 9;

        c.Page(p =>
        {
            p.Size(PageSizes.A4.Landscape());
            p.Margin(24);
            p.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(fontSize).FontColor("#0F172A"));

            p.Header().Column(col =>
            {
                col.Item().Text("SALARY SHEET SELECTION").FontSize(16).Bold().FontColor("#2563EB");
                col.Item().Text($"{monthName} {year}").FontSize(10).FontColor("#64748B");
                col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#E2E8F0");
            });

            p.Content().PaddingVertical(10).Table(t =>
            {
                var groupedRows = rows
                    .GroupBy(r => PdfGroupName(r.GroupName))
                    .ToList();
                var showGroupTotals = rows.Any(r => !string.IsNullOrWhiteSpace(r.GroupName));
                var showTotals = columns.Any(column => column.IncludeInTotal);

                t.ColumnsDefinition(cd =>
                {
                    foreach (var column in columns)
                    {
                        var width = column.AlignRight ? 2 : Math.Max(2, Math.Min(4, column.Header.Length / 6 + 1));
                        cd.RelativeColumn(width);
                    }
                });

                t.Header(h =>
                {
                    foreach (var column in columns)
                    {
                        var cell = PdfTableCell(h.Cell(), cellPadding, "#F1F5F9");
                        if (column.AlignRight)
                            cell.AlignRight().Text(column.Header).Bold();
                        else
                            cell.Text(column.Header).Bold();
                    }
                });

                foreach (var group in groupedRows)
                {
                    if (showGroupTotals)
                    {
                        PdfTableCell(t.Cell().ColumnSpan((uint)columns.Count), cellPadding, "#E0F2FE")
                            .Text(group.Key).Bold().FontColor("#0F172A");
                    }

                    foreach (var row in group)
                    {
                        for (var i = 0; i < columns.Count; i++)
                        {
                            var value = i < row.Values.Count ? row.Values[i] : string.Empty;
                            var cell = PdfTableCell(t.Cell(), cellPadding);
                            if (columns[i].AlignRight)
                                cell.AlignRight().Text(value);
                            else
                                cell.Text(value);
                        }
                    }

                    if (showGroupTotals && showTotals)
                        SelectionTotalRow(t, columns, group, "Group Total", "#F8FAFC", "#0F172A", cellPadding);
                }

                if (showTotals)
                    SelectionTotalRow(t, columns, rows, showGroupTotals ? "Final Total" : "TOTAL", "#2563EB", "#FFFFFF", cellPadding);
            });

            p.Footer().AlignRight().Text(x =>
            {
                x.Span("Page ").FontSize(8).FontColor("#64748B");
                x.CurrentPageNumber().FontSize(8).FontColor("#64748B");
                x.Span(" / ").FontSize(8).FontColor("#64748B");
                x.TotalPages().FontSize(8).FontColor("#64748B");
            });
        });
    });

    private static IContainer SummaryHeaderCell(IContainer cell)
        => PdfTableCell(cell, 6, "#F1F5F9");

    private static IContainer PdfTableCell(IContainer cell, float padding, string? background = null)
    {
        var styled = cell.Border(0.5f).BorderColor(TableBorderColor);
        if (!string.IsNullOrWhiteSpace(background))
            styled = styled.Background(background);

        return styled.Padding(padding);
    }

    private static void SummaryTotalRow(
        QuestPDF.Fluent.TableDescriptor table,
        string label,
        IEnumerable<MonthlySummaryRow> rows,
        string background,
        string fontColor)
    {
        var rowList = rows.ToList();
        var totalBase = rowList.Sum(row => row.BaseSalary);
        var totalSalaryPaid = rowList.Sum(row => row.SalaryPaid);
        var totalDaysAbsent = rowList.Sum(row => row.DaysAbsent);
        var totalDeduction = rowList.Sum(row => row.Deduction);
        var totalPf = rowList.Sum(row => row.PfDeduction);
        var totalEsic = rowList.Sum(row => row.EsicDeduction);
        var totalTds = rowList.Sum(row => row.TdsDeduction);
        var totalAdvance = rowList.Sum(row => row.AdvanceDeduction);
        var totalNet = rowList.Sum(row => row.NetSalary);

        PdfTableCell(table.Cell(), 6, background).Text(label).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalBase.ToString("N2")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalDaysAbsent.ToString(CultureInfo.CurrentCulture)).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalDeduction.ToString("N2")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalSalaryPaid.ToString("N0")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalPf.ToString("N2")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalEsic.ToString("N2")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalTds.ToString("N2")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalAdvance.ToString("N2")).Bold().FontColor(fontColor);
        PdfTableCell(table.Cell(), 6, background).AlignRight().Text(totalNet.ToString("N2")).Bold().FontColor(fontColor);
    }

    private static void SelectionTotalRow(
        QuestPDF.Fluent.TableDescriptor table,
        IReadOnlyList<SalarySheetSelectionColumn> columns,
        IEnumerable<SalarySheetSelectionRow> rows,
        string label,
        string background,
        string fontColor,
        float cellPadding)
    {
        var labelColumn = columns
            .Select((column, index) => new { column, index })
            .FirstOrDefault(item => !item.column.AlignRight)?.index;

        for (var i = 0; i < columns.Count; i++)
        {
            var cell = PdfTableCell(table.Cell(), cellPadding, background);
            if (labelColumn.HasValue && i == labelColumn.Value)
            {
                cell.Text(label).Bold().FontColor(fontColor);
                continue;
            }

            if (!columns[i].IncludeInTotal)
            {
                cell.Text(string.Empty);
                continue;
            }

            var total = CalculateSelectionTotal(rows, i, columns[i]);
            if (columns[i].AlignRight)
                cell.AlignRight().Text(total).Bold().FontColor(fontColor);
            else
                cell.Text(total).Bold().FontColor(fontColor);
        }
    }

    private static string CalculateSelectionTotal(
        IEnumerable<SalarySheetSelectionRow> rows,
        int columnIndex,
        SalarySheetSelectionColumn column)
    {
        decimal total = 0;
        var hasValue = false;
        var hasDecimalValue = false;
        var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        foreach (var row in rows)
        {
            if (columnIndex >= row.Values.Count)
                continue;

            var value = row.Values[columnIndex];
            if (!decimal.TryParse(value, NumberStyles.Currency, CultureInfo.CurrentCulture, out var number))
                continue;

            total += number;
            hasValue = true;
            hasDecimalValue |= value.Contains(decimalSeparator, StringComparison.Ordinal);
        }

        if (!hasValue)
            return string.Empty;

        return IsWholeNumberTotalColumn(column)
            ? total.ToString("N0", CultureInfo.CurrentCulture)
            : hasDecimalValue
                ? total.ToString("N2", CultureInfo.CurrentCulture)
                : total.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static bool IsWholeNumberTotalColumn(SalarySheetSelectionColumn column)
        => column.Key is "absent" or "salaryPaid";

    private static string PdfGroupName(string? groupName)
        => string.IsNullOrWhiteSpace(groupName) ? "Other" : groupName.Trim();

    private static IDocument BuildLedgerDocument(Employee emp, IReadOnlyList<LedgerRow> rows, decimal balance) => Document.Create(c =>
    {
        c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(30);
            p.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(10));

            p.Header().Column(col =>
            {
                col.Item().Text("ADVANCE LEDGER").FontSize(18).Bold().FontColor("#2563EB");
                col.Item().Text(emp.Name).FontSize(13).Bold();
                col.Item().Text($"Outstanding balance: ₹ {balance:N2}").FontColor("#64748B");
                col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#E2E8F0");
            });

            p.Content().PaddingVertical(12).Column(col =>
            {
                if (rows.Count == 0)
                {
                    col.Item()
                       .PaddingTop(32)
                       .AlignCenter()
                       .Text("No advance entries found for this employee.")
                       .FontSize(12).FontColor("#94A3B8").Italic();
                }
                else
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(2);
                            cd.RelativeColumn(4);
                            cd.RelativeColumn(2);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Background("#F1F5F9").Padding(6).Text("Date").Bold();
                            h.Cell().Background("#F1F5F9").Padding(6).Text("Type").Bold();
                            h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Amount").Bold();
                            h.Cell().Background("#F1F5F9").Padding(6).Text("Note").Bold();
                            h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Balance").Bold();
                        });

                        foreach (var r in rows)
                        {
                            t.Cell().Padding(6).Text(r.Entry.Date.ToString("yyyy-MM-dd"));
                            t.Cell().Padding(6).Text(r.Entry.EntryType.ToString());
                            t.Cell().Padding(6).AlignRight().Text(r.Entry.Amount.ToString("N2"));
                            t.Cell().Padding(6).Text(r.Entry.Note ?? "");
                            t.Cell().Padding(6).AlignRight().Text(r.RunningBalance.ToString("N2")).Bold();
                        }
                    });
                }
            });
        });
    });

    private static IDocument BuildSalaryRevisionDocument(IReadOnlyList<SalaryRevisionReportRow> rows) => Document.Create(c =>
    {
        c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(30);
            p.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(10).FontColor("#0F172A"));

            p.Header().Column(col =>
            {
                col.Item().Text("SALARY REVISIONS REPORT").FontSize(18).Bold().FontColor("#2563EB");
                col.Item().Text($"Generated {DateTime.Now:dd MMM yyyy}").FontSize(11).FontColor("#64748B");
                col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#E2E8F0");
            });

            p.Content().PaddingVertical(12).Column(col =>
            {
                if (rows.Count == 0)
                {
                    col.Item().PaddingTop(32).AlignCenter()
                       .Text("No salary revisions found.")
                       .FontSize(12).FontColor("#94A3B8").Italic();
                    return;
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(3);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Background("#F1F5F9").Padding(6).Text("Employee").Bold();
                        h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Old Salary").Bold();
                        h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("New Salary").Bold();
                        h.Cell().Background("#F1F5F9").Padding(6).Text("Date").Bold();
                        h.Cell().Background("#F1F5F9").Padding(6).Text("Note").Bold();
                    });

                    foreach (var r in rows)
                    {
                        t.Cell().Padding(6).Text(r.EmployeeName);
                        t.Cell().Padding(6).AlignRight().Text(r.OldSalary.ToString("N2"));
                        t.Cell().Padding(6).AlignRight().Text(r.NewSalary.ToString("N2")).Bold();
                        t.Cell().Padding(6).Text(r.ChangedAt.ToString("dd MMM yyyy"));
                        t.Cell().Padding(6).Text(r.Note ?? "");
                    }
                });
            });
        });
    });
}
