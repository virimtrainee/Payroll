using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace SalaryManager.App.Converters;

public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string ?? string.Empty;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
