using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace SalaryManager.App.Behaviors;

public sealed class EnterKeyActionBehavior : Behavior<UIElement>
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(EnterKeyActionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(EnterKeyActionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FocusTargetProperty =
        DependencyProperty.Register(
            nameof(FocusTarget),
            typeof(FrameworkElement),
            typeof(EnterKeyActionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.Register(
            nameof(SelectAllOnFocus),
            typeof(bool),
            typeof(EnterKeyActionBehavior),
            new PropertyMetadata(true));

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

    public FrameworkElement? FocusTarget
    {
        get => (FrameworkElement?)GetValue(FocusTargetProperty);
        set => SetValue(FocusTargetProperty, value);
    }

    public bool SelectAllOnFocus
    {
        get => (bool)GetValue(SelectAllOnFocusProperty);
        set => SetValue(SelectAllOnFocusProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewKeyDown += OnPreviewKeyDown;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewKeyDown -= OnPreviewKeyDown;
        base.OnDetaching();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (Command is { } command && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);

        if (FocusTarget is { } focusTarget)
        {
            focusTarget.Focus();
            if (SelectAllOnFocus && focusTarget is TextBox textBox)
                textBox.SelectAll();
        }

        e.Handled = true;
    }
}
