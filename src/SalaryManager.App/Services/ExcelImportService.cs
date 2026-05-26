using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Validation;

namespace SalaryManager.App.Services;

public sealed record ImportedEmployeeRow(
    string Name,
    decimal BaseSalary,
    string? AccountNumber,
    string? IfscCode,
    PaymentMode PaymentMode);

public sealed record EmployeeImportReadResult(
    IReadOnlyList<ImportedEmployeeRow> Rows,
    IReadOnlyList<ValidationIssue> RowErrors,
    string? Error)
{
    public bool HasRowErrors => RowErrors.Count > 0;

    public IReadOnlyList<ValidationIssue> Issues => RowErrors;

    public string RowErrorsToDisplayString()
        => ValidationResult.FromErrors(RowErrors).ToDisplayString();

    public static EmployeeImportReadResult Failed(string error)
        => new([], [], error);
}

public class ExcelImportService
{
    // Known aliases for each column - all matched case-insensitively.
    private static readonly string[] NameAliases = ["BNF_NAME", "NAME", "EMPLOYEE_NAME", "EMPLOYEE"];
    private static readonly string[] SalaryAliases = ["AMOUNT", "SALARY", "BASE_SALARY", "NET_SALARY"];
    private static readonly string[] AccountAliases = ["BENE_ACC_NO", "ACCOUNT_NO", "ACCOUNT", "ACC_NO", "ACCOUNT_NUMBER"];
    private static readonly string[] IfscAliases = ["BENE_IFSC", "IFSC", "IFSC_CODE"];
    private static readonly string[] ModeAliases = ["PYMT_MODE", "PAYMENT_MODE", "MODE", "BANK_TYPE"];

    public EmployeeImportReadResult ReadEmployees(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws is null)
                return EmployeeImportReadResult.Failed("The workbook does not contain any worksheets.");

            return ReadWorksheet(ws);
        }
        catch (Exception ex)
        {
            return EmployeeImportReadResult.Failed(
                "Could not read the Excel workbook. The file may be corrupt or not a supported .xlsx file. " +
                ex.Message);
        }
    }

    private static EmployeeImportReadResult ReadWorksheet(IXLWorksheet ws)
    {
        var headerRow = ws.FirstRowUsed();
        if (headerRow is null)
            return EmployeeImportReadResult.Failed("The file appears to be empty.");

        // Build case-insensitive header-to-column-number map.
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = CellText(cell)?.Trim();
            if (!string.IsNullOrEmpty(key))
                headers.TryAdd(key, cell.Address.ColumnNumber);
        }

        var nameCol = Find(headers, NameAliases);
        var salaryCol = Find(headers, SalaryAliases);
        var accountCol = Find(headers, AccountAliases);
        var ifscCol = Find(headers, IfscAliases);
        var modeCol = Find(headers, ModeAliases);

        if (nameCol is null)
            return EmployeeImportReadResult.Failed("Could not find an employee name column. " +
                                                   "Expected one of: " + string.Join(", ", NameAliases) + ".");

        if (salaryCol is null)
            return EmployeeImportReadResult.Failed("Could not find a salary column. " +
                                                   "Expected one of: " + string.Join(", ", SalaryAliases) + ".");

        var rows = new List<ImportedEmployeeRow>();
        var rowErrors = new List<ValidationIssue>();
        var seenImportNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ws.RowsUsed().Where(row => row.RowNumber() > headerRow.RowNumber()))
        {
            if (!HasRelevantValue(row, nameCol, salaryCol, accountCol, ifscCol, modeCol))
                continue;

            var rowNumber = row.RowNumber();
            var errorsBefore = rowErrors.Count;

            var name = Cell(row, nameCol);
            var salary = 0m;
            var salaryCell = row.Cell(salaryCol.Value);
            if (IsBlank(salaryCell))
            {
                rowErrors.Add(new ValidationIssue(
                    "Base salary",
                    "is required.",
                    "salary_required",
                    rowNumber));
            }
            else if (!TryReadDecimal(salaryCell, out salary))
            {
                rowErrors.Add(new ValidationIssue(
                    "Base salary",
                    "must be a valid number.",
                    "salary_invalid",
                    rowNumber));
            }

            if (!string.IsNullOrWhiteSpace(name) && !seenImportNames.Add(name.Trim()))
            {
                rowErrors.Add(new ValidationIssue(
                    "Name",
                    "is duplicated in the import file.",
                    "name_duplicate_import",
                    rowNumber));
            }

            var account = Cell(row, accountCol);
            var ifsc = Cell(row, ifscCol);
            var mode = ParsePaymentMode(Cell(row, modeCol), account, ifsc);

            rowErrors.AddRange(EmployeeValidator.Validate(
                new EmployeeValidationInput(name, salary, mode, account, ifsc),
                rowNumber: rowNumber).Errors);

            if (rowErrors.Count != errorsBefore)
                continue;

            rows.Add(new ImportedEmployeeRow(
                Name: name!.Trim(),
                BaseSalary: salary,
                AccountNumber: string.IsNullOrWhiteSpace(account) ? null : account.Trim(),
                IfscCode: string.IsNullOrWhiteSpace(ifsc) ? null : ifsc.Trim().ToUpperInvariant(),
                PaymentMode: mode));
        }

        return new EmployeeImportReadResult(rows, rowErrors, null);
    }

    private static int? Find(Dictionary<string, int> headers, string[] aliases)
    {
        foreach (var alias in aliases)
            if (headers.TryGetValue(alias, out var col)) return col;
        return null;
    }

    private static string? Cell(IXLRow row, int? col)
        => col is null ? null : CellText(row.Cell(col.Value))?.Trim().NullIfEmpty();

    private static string? CellText(IXLCell cell)
    {
        if (IsBlank(cell))
            return null;

        if (cell.Value.IsNumber)
            return cell.Value.GetNumber().ToString("0.################", CultureInfo.InvariantCulture);

        return cell.TryGetValue<string>(out var value)
            ? value.Trim().NullIfEmpty()
            : cell.Value.ToString().Trim().NullIfEmpty();
    }

    private static bool TryReadDecimal(IXLCell cell, out decimal value)
    {
        if (IsBlank(cell))
        {
            value = 0m;
            return false;
        }

        if (cell.TryGetValue(out value))
            return true;

        var text = CellText(cell);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;
        return decimal.TryParse(text, styles, CultureInfo.CurrentCulture, out value) ||
               decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
    }

    private static bool HasRelevantValue(IXLRow row, params int?[] columns)
        => columns.Where(col => col is not null)
                  .Any(col => !IsBlank(row.Cell(col!.Value)));

    private static bool IsBlank(IXLCell cell)
        => cell.Value.IsBlank || string.IsNullOrWhiteSpace(cell.Value.ToString());

    private static PaymentMode ParsePaymentMode(string? value, string? account, string? ifsc)
        => value?.Trim().ToUpperInvariant() switch
        {
            "CASH" => PaymentMode.Cash,
            "FT" or "ICICI" or "ICICI BANK" or "ICICIBANK" => PaymentMode.IciciBank,
            "NEFT" or "OTHER" or "OTHER BANK" or "OTHERBANK" => PaymentMode.OtherBank,
            _ when string.IsNullOrWhiteSpace(account) && string.IsNullOrWhiteSpace(ifsc) => PaymentMode.Cash,
            _ when ifsc?.Trim().StartsWith("ICIC0", StringComparison.OrdinalIgnoreCase) == true => PaymentMode.IciciBank,
            _ => PaymentMode.OtherBank
        };
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
