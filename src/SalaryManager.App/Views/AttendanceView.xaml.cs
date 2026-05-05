using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SalaryManager.App.Views;

public partial class AttendanceView : UserControl
{
    public AttendanceView() => InitializeComponent();

    // Begin editing as soon as a cell becomes current — keyboard-first behaviour
    private void AttendanceGrid_CurrentCellChanged(object sender, EventArgs e)
    {
        if (sender is not DataGrid dg) return;
        Application.Current.Dispatcher.InvokeAsync(
            () => dg.BeginEdit(),
            DispatcherPriority.Background);
    }
}
