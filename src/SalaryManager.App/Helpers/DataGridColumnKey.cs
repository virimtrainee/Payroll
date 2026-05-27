using System.Windows;

namespace SalaryManager.App.Helpers;

public static class DataGridColumnKey
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(DataGridColumnKey),
            new PropertyMetadata(null));

    public static void SetKey(DependencyObject element, string? value) =>
        element.SetValue(KeyProperty, value);

    public static string? GetKey(DependencyObject element) =>
        element.GetValue(KeyProperty) as string;
}
