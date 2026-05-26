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
    decimal SalaryAdvanceDeduction);

public record MonthlySummaryRow(
    string EmployeeName,
    decimal BaseSalary,
    int DaysAbsent,
    decimal Deduction,
    decimal EsicDeduction,
    decimal PfDeduction,
    decimal TdsDeduction,
    decimal AdvanceDeduction,
    decimal NetSalary);

public class PdfSlipService
{
    public string GenerateSlip(SalarySlipData data, string? outputPath = null)
    {
        outputPath ??= DefaultSlipPath(data);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        BuildSlipDocument(data).GeneratePdf(outputPath);
        return outputPath;
    }

    public string GenerateMonthlySummary(int year, int month, IReadOnlyList<MonthlySummaryRow> rows, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        BuildSummaryDocument(year, month, rows).GeneratePdf(outputPath);
        return outputPath;
    }

    public string GenerateAdvanceLedger(Employee emp, IReadOnlyList<LedgerRow> rows, decimal balance, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        BuildLedgerDocument(emp, rows, balance).GeneratePdf(outputPath);
        return outputPath;
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
        c.Page(p =>
        {
            p.Size(PageSizes.A5);
            p.Margin(28);
            p.PageColor(Colors.White);
            p.DefaultTextStyle(t => t.FontSize(10).FontColor("#0F172A"));

            p.Header().Column(col =>
            {
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
                    row.RelativeItem().AlignRight().Column(c2 =>
                    {
                        c2.Item().Text("EMPLOYEE ID").FontSize(8).FontColor("#64748B").Bold();
                        c2.Item().Text($"#{d.Employee.Id:D4}").FontSize(14).Bold();
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
                    if (d.Breakdown.EsicDeduction > 0)
                        DetailRow(box, "ESIC deduction", $"- {d.Breakdown.EsicDeduction:N2}");
                    if (d.Breakdown.PfDeduction > 0)
                        DetailRow(box, "PF deduction", $"- {d.Breakdown.PfDeduction:N2}");
                    if (d.Breakdown.TdsDeduction > 0)
                        DetailRow(box, "TDS deduction", $"- {d.Breakdown.TdsDeduction:N2}");
                    if (d.SalaryAdvanceDeduction > 0)
                        DetailRow(box, "Advance deduction", $"- {d.SalaryAdvanceDeduction:N2}");
                });

                col.Item().PaddingTop(8).Background("#2563EB").Padding(12).Row(r =>
                {
                    var payable = d.Breakdown.NetSalary - Math.Max(0, d.SalaryAdvanceDeduction);
                    r.RelativeItem().Text("PAYABLE NET").FontSize(11).Bold().FontColor("white");
                    r.ConstantItem(140).AlignRight().Text($"₹ {payable:N2}")
                        .FontSize(16).Bold().FontColor("white");
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
                    r.RelativeItem().Text("Employee Signature").FontSize(9).FontColor("#64748B");
                    r.RelativeItem().AlignRight().Text("Authorized Signatory").FontSize(9).FontColor("#64748B");
                });
            });
        });
    });

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
            p.DefaultTextStyle(t => t.FontSize(10).FontColor("#0F172A"));

            p.Header().Column(col =>
            {
                col.Item().Text("MONTHLY PAYROLL SUMMARY").FontSize(18).Bold().FontColor("#2563EB");
                col.Item().Text($"{monthName} {year}").FontSize(11).FontColor("#64748B");
                col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#E2E8F0");
            });

            p.Content().PaddingVertical(12).Table(t =>
            {
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
                });

                t.Header(h =>
                {
                    h.Cell().Background("#F1F5F9").Padding(6).Text("Employee").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Base Salary").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Absent").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Deduction").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("ESIC").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("PF").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("TDS").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Adv. Ded").Bold();
                    h.Cell().Background("#F1F5F9").Padding(6).AlignRight().Text("Net Salary").Bold();
                });

                decimal totalBase = 0, totalDed = 0, totalEsic = 0, totalPf = 0, totalTds = 0, totalAdvance = 0, totalNet = 0;
                foreach (var r in rows)
                {
                    t.Cell().Padding(6).Text(r.EmployeeName);
                    t.Cell().Padding(6).AlignRight().Text(r.BaseSalary.ToString("N2"));
                    t.Cell().Padding(6).AlignRight().Text(r.DaysAbsent.ToString());
                    t.Cell().Padding(6).AlignRight().Text(r.Deduction.ToString("N2"));
                    t.Cell().Padding(6).AlignRight().Text(r.EsicDeduction.ToString("N2"));
                    t.Cell().Padding(6).AlignRight().Text(r.PfDeduction.ToString("N2"));
                    t.Cell().Padding(6).AlignRight().Text(r.TdsDeduction.ToString("N2"));
                    t.Cell().Padding(6).AlignRight().Text(r.AdvanceDeduction.ToString("N2"));
                    t.Cell().Padding(6).AlignRight().Text(r.NetSalary.ToString("N2")).Bold();
                    totalBase += r.BaseSalary;
                    totalDed += r.Deduction;
                    totalEsic += r.EsicDeduction;
                    totalPf += r.PfDeduction;
                    totalTds += r.TdsDeduction;
                    totalAdvance += r.AdvanceDeduction;
                    totalNet += r.NetSalary;
                }

                t.Cell().Background("#2563EB").Padding(6).Text("TOTAL").Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalBase.ToString("N2")).Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text("").FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalDed.ToString("N2")).Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalEsic.ToString("N2")).Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalPf.ToString("N2")).Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalTds.ToString("N2")).Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalAdvance.ToString("N2")).Bold().FontColor("white");
                t.Cell().Background("#2563EB").Padding(6).AlignRight().Text(totalNet.ToString("N2")).Bold().FontColor("white");
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

    private static IDocument BuildLedgerDocument(Employee emp, IReadOnlyList<LedgerRow> rows, decimal balance) => Document.Create(c =>
    {
        c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(30);
            p.DefaultTextStyle(t => t.FontSize(10));

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
}
