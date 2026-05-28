using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SalaryManager.App.Helpers;

internal static class UiCommandScheduler
{
    private static readonly object OnceGate = new();
    private static readonly ConditionalWeakTable<ICommand, object> OnceCommands = new();
    private static readonly object OnceMarker = new();

    public static bool ExecuteDeferredIfPossible(
        ICommand? command,
        object? parameter = null,
        DispatcherObject? dispatcherSource = null,
        Action? onExecuted = null,
        Action? onSkipped = null)
    {
        if (command is null || !command.CanExecute(parameter))
            return false;

        void Execute()
        {
            if (!command.CanExecute(parameter))
            {
                onSkipped?.Invoke();
                return;
            }

            command.Execute(parameter);
            onExecuted?.Invoke();
        }

        var dispatcher = dispatcherSource?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            Execute();
            return true;
        }

        dispatcher.BeginInvoke((Action)Execute, DispatcherPriority.Background);
        return true;
    }

    public static bool ExecuteDeferredOnceIfPossible(
        ICommand? command,
        object? parameter = null,
        DispatcherObject? dispatcherSource = null)
    {
        if (command is null || !command.CanExecute(parameter))
            return false;

        lock (OnceGate)
        {
            if (OnceCommands.TryGetValue(command, out _))
                return false;

            OnceCommands.Add(command, OnceMarker);
        }

        return ExecuteDeferredIfPossible(command, parameter, dispatcherSource);
    }
}
