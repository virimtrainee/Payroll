using System;
using System.Globalization;
using System.Windows.Data;

namespace SalaryManager.App.Converters;

public sealed class IsNonPositiveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => TryGetDecimal(value, out var amount) && amount <= 0m;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

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
            case double d:
                amount = (decimal)d;
                return true;
            case float f:
                amount = (decimal)f;
                return true;
            case long l:
                amount = l;
                return true;
            default:
                return decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.CurrentCulture, out amount);
        }
    }
}
