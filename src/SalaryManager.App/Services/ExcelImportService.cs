using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using SalaryManager.Data.Entities;

namespace SalaryManager.App.Services;

public record ImportedEmployeeRow(
    string Name,
    decimal BaseSalary,
    string? AccountNumber,
    string? IfscCode,
    PaymentMode PaymentMode);

public class ExcelImportService
{
    // Known aliases for each column — all matched case-insensitively
    private static readonly string[] NameAliases    = ["BNF_NAME", "NAME", "EMPLOYEE_NAME", "EMPLOYEE"];
    private static readonly string[] SalaryAliases  = ["AMOUNT", "SALARY", "BASE_SALARY", "NET_SALARY"];
    private static readonly string[] AccountAliases = ["BENE_ACC_NO", "ACCOUNT_NO", "ACCOUNT", "ACC_NO", "ACCOUNT_NUMBER"];
    private static readonly string[] IfscAliases    = ["BENE_IFSC", "IFSC", "IFSC_CODE"];
    private static readonly string[] ModeAliases    = ["PYMT_MODE", "PAYMENT_MODE", "MODE", "BANK_TYPE"];

    public (IReadOnlyList<ImportedEmployeeRow> Rows, string? Error) ReadEmployees(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(1);

        var headerRow = ws.FirstRowUsed();
        if (headerRow is null)
            return ([], "The file appears to be empty.");

        // Build case-insensitive header → column-number map
        var headers = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = cell.Value.ToString().Trim();
            if (!string.IsNullOrEmpty(key))
                headers.TryAdd(key, cell.Address.ColumnNumber);
        }

        var nameCol   = Find(headers, NameAliases);
        var salaryCol = Find(headers, SalaryAliases);
        var accountCol= Find(headers, AccountAliases);
        var ifscCol   = Find(headers, IfscAliases);
        var modeCol   = Find(headers, ModeAliases);

        if (nameCol is null)
            return ([], "Could not find an employee name column. " +
                        "Expected one of: " + string.Join(", ", NameAliases) + ".");

        var rows = new List<ImportedEmployeeRow>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var name = Cell(row, nameCol);
            if (string.IsNullOrWhiteSpace(name)) continue;

            decimal.TryParse(Cell(row, salaryCol), out var salary);

            var account = Cell(row, accountCol);
            var ifsc    = Cell(row, ifscCol);

            var mode = Cell(row, modeCol)?.ToUpperInvariant() switch
            {
                "FT"   => PaymentMode.IciciBank,
                "CASH" => PaymentMode.Cash,
                _      => PaymentMode.OtherBank
            };

            rows.Add(new ImportedEmployeeRow(
                Name:          name.Trim(),
                BaseSalary:    salary,
                AccountNumber: string.IsNullOrWhiteSpace(account) ? null : account.Trim(),
                IfscCode:      string.IsNullOrWhiteSpace(ifsc)    ? null : ifsc.Trim().ToUpperInvariant(),
                PaymentMode:   mode));
        }

        return (rows, null);
    }

    private static int? Find(Dictionary<string, int> headers, string[] aliases)
    {
        foreach (var alias in aliases)
            if (headers.TryGetValue(alias, out var col)) return col;
        return null;
    }

    private static string? Cell(IXLRow row, int? col)
        => col is null ? null : row.Cell(col.Value).Value.ToString().Trim().NullIfEmpty();
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
