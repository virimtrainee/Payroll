using System;
using System.Globalization;
using System.Windows.Data;

namespace SalaryManager.App.Converters;

public sealed class IndianCurrencyConverter : IValueConverter
{
    private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (!TryGetDecimal(value, out var amount)) return value;

        var spec = parameter?.ToString() ?? "2";
        var includeSymbol = !spec.StartsWith("plain", StringComparison.OrdinalIgnoreCase);
        if (!includeSymbol)
            spec = spec["plain".Length..];

        var decimals = int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 4)
            : 2;

        var text = amount.ToString($"N{decimals}", IndianCulture);
        return includeSymbol ? $"₹{text}" : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static bool TryGetDecimal(object value, out decimal amount)
    {
        switch (value)
        {
            case decimal d:
                amount = d;
                return true;
            case int i:
                amount = i;
                return true;
            case double dbl:
                amount = (decimal)dbl;
                return true;
            case float f:
                amount = (decimal)f;
                return true;
            case long l:
                amount = l;
                return true;
            default:
                return decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.CurrentCulture, out amount);
        }
    }
}
