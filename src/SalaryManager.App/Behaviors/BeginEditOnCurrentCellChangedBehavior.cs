using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace SalaryManager.App.Behaviors;

public sealed class BeginEditOnCurrentCellChangedBehavior : Behavior<DataGrid>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.CurrentCellChanged += OnCurrentCellChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.CurrentCellChanged -= OnCurrentCellChanged;
        base.OnDetaching();
    }

    private void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (AssociatedObject is not { } dataGrid) return;

        Application.Current.Dispatcher.InvokeAsync(
            () => dataGrid.BeginEdit(),
            DispatcherPriority.Background);
    }
}
