using System.Windows;
using System.Windows.Controls;
using SalaryManager.App.Helpers;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Views;

public partial class ReportsView : UserControl
{
    private bool _initialized;

    public ReportsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        if (DataContext is not ReportsViewModel vm) return;
        _initialized = true;
        UiCommandScheduler.ExecuteDeferredOnceIfPossible(vm.LoadCommand, dispatcherSource: this);
    }
}
