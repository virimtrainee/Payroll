using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Converters;

public sealed class SalaryGroupSummaryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup group)
            return string.Empty;

        var rows = group.Items.OfType<SalaryRowVm>().ToList();
        var label = rows.Count == 1 ? "employee" : "employees";
        var baseTotal = rows.Sum(r => r.BaseSalary);

        return $"{rows.Count} {label}  Base \u20B9{baseTotal.ToString("N0", CultureInfo.GetCultureInfo("en-IN"))}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
