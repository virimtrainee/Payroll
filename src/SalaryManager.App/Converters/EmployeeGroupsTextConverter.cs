using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using SalaryManager.Data.Entities;

namespace SalaryManager.App.Converters;

public sealed class EmployeeGroupsTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<EmployeeGroupMembership> memberships)
            return "Other";

        var names = memberships
            .Select(m => m.EmployeeGroup?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n)
            .ToList();

        return names.Count == 0 ? "Other" : string.Join(", ", names);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
