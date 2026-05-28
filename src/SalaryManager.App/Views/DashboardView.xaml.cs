using System.Windows;
using System.Windows.Controls;
using SalaryManager.App.Helpers;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Views;

public partial class DashboardView : UserControl
{
    private bool _initialized;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        if (DataContext is not DashboardViewModel vm) return;
        _initialized = true;
        UiCommandScheduler.ExecuteDeferredOnceIfPossible(vm.LoadCommand, dispatcherSource: this);
    }
}
