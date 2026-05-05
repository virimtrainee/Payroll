using System;
using System.Globalization;
using System.Windows.Data;

namespace SalaryManager.App.Converters;

[ValueConversion(typeof(decimal), typeof(bool))]
public class IsNegativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is decimal d && d < 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
