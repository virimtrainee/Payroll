using System.Windows;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using SalaryManager.App.Helpers;

namespace SalaryManager.App.Behaviors;

public sealed class LoadedCommandBehavior : Behavior<FrameworkElement>
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(LoadedCommandBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(LoadedCommandBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ExecuteOnceProperty =
        DependencyProperty.Register(
            nameof(ExecuteOnce),
            typeof(bool),
            typeof(LoadedCommandBehavior),
            new PropertyMetadata(true));

    private bool _hasExecuted;

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool ExecuteOnce
    {
        get => (bool)GetValue(ExecuteOnceProperty);
        set => SetValue(ExecuteOnceProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ExecuteOnce && _hasExecuted) return;
        if (Command is not { } command || !command.CanExecute(CommandParameter)) return;

        _hasExecuted = true;
        var scheduled = ExecuteOnce
            ? UiCommandScheduler.ExecuteDeferredOnceIfPossible(command, CommandParameter, AssociatedObject)
            : UiCommandScheduler.ExecuteDeferredIfPossible(command, CommandParameter, AssociatedObject);

        if (!scheduled)
            _hasExecuted = false;
    }
}
