using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Views;

public partial class AdvancesView : UserControl
{
    private bool _initialized;

    public AdvancesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        if (DataContext is not AdvancesViewModel vm) return;
        _initialized = true;
        vm.LoadCommand.Execute(null);
    }

    // Enter on the Amount box moves focus to Narration
    private void AmountBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NarrationBox.Focus();
        NarrationBox.SelectAll();
        e.Handled = true;
    }

    // Enter on Narration submits and returns focus to Amount
    private void NarrationBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is AdvancesViewModel vm && vm.AddEntryCommand.CanExecute(null))
            vm.AddEntryCommand.Execute(null);
        AmountBox.Focus();
        AmountBox.SelectAll();
        e.Handled = true;
    }
}
