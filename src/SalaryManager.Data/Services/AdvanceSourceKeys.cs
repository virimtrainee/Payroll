using System.Text.RegularExpressions;

namespace SalaryManager.Data.Services;

public static partial class AdvanceSourceKeys
{
    public static string Salary(int year, int month) => $"SAL:{year:0000}-{month:00}";

    public static bool IsSalary(string? sourceKey)
        => sourceKey is not null && SalaryKeyRegex().IsMatch(sourceKey);

    public static bool TryParseSalary(string? sourceKey, out int year, out int month)
    {
        year = 0;
        month = 0;

        if (!IsSalary(sourceKey))
            return false;

        year = int.Parse(sourceKey.AsSpan(4, 4), System.Globalization.CultureInfo.InvariantCulture);
        month = int.Parse(sourceKey.AsSpan(9, 2), System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    [GeneratedRegex(@"^SAL:\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex SalaryKeyRegex();
}
