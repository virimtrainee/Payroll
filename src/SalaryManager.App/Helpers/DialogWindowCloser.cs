using System.Windows;
using System.Windows.Controls;

namespace SalaryManager.App.Helpers;

internal static class DialogWindowCloser
{
    public static void Close(FrameworkElement source, bool result)
    {
        var window = Window.GetWindow(source);
        if (window is not null && IsStandaloneDialogWindow(window, source))
        {
            window.DialogResult = result;
        }
    }

    private static bool IsStandaloneDialogWindow(Window window, FrameworkElement source)
        => ReferenceEquals(window.Content, source)
           || window.Content is Border { Child: var child } && ReferenceEquals(child, source);
}
