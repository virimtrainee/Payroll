using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Services;

public class DialogService
{
    public virtual bool Confirm(string message, string title = "Confirm")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public virtual bool ConfirmDestructive(string message, string title = "Confirm Delete")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public virtual bool ConfirmWarning(string message, string title = "Confirm")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public virtual void Info(string message, string title = "Information")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public virtual void Error(string message, string title = "Error")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public virtual Task<bool> ConfirmAsync(string message, string title = "Confirm")
        => ShowChoiceAsync(message, title, "No", "Yes", false);

    public virtual Task<bool> ConfirmDestructiveAsync(string message, string title = "Confirm Delete")
        => ShowChoiceAsync(message, title, "Cancel", "Delete", true);

    public virtual Task<bool> ConfirmWarningAsync(string message, string title = "Confirm", string confirmText = "Continue")
        => ShowChoiceAsync(message, title, "Cancel", confirmText, false, useWarningFallback: true);

    public virtual Task InfoAsync(string message, string title = "Information")
        => ShowNoticeAsync(message, title, "OK", false);

    public virtual Task ErrorAsync(string message, string title = "Error")
        => ShowNoticeAsync(message, title, "OK", true);

    public virtual string? AskSavePath(string filter, string defaultName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public virtual string? AskOpenPath(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public virtual async Task<decimal?> AskNonNegativeDecimalAsync(
        string title,
        string label,
        decimal defaultValue = 0m,
        string confirmText = "Apply")
    {
        var app = Application.Current;
        if (app is null)
            return null;

        if (!app.Dispatcher.CheckAccess())
        {
            var operation = app.Dispatcher.InvokeAsync(() => AskNonNegativeDecimalAsync(title, label, defaultValue, confirmText));
            return await await operation.Task;
        }

        decimal? amount = null;
        var amountBox = new TextBox
        {
            Text = defaultValue.ToString("0.##", CultureInfo.CurrentCulture),
            MinWidth = 240,
            Margin = new Thickness(0, 4, 0, 8)
        };
        var errorText = new TextBlock
        {
            Foreground = FindBrush("DangerBrush", Brushes.Firebrick),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var applyButton = CreateButton(confirmText, true, false);
        var cancelButton = CreateButton("Cancel", false, false);
        applyButton.Click += (_, _) =>
        {
            if (!decimal.TryParse(amountBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
            {
                errorText.Text = "Enter a valid amount.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            if (parsed < 0m)
            {
                errorText.Text = "Amount cannot be negative.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            amount = parsed;
            CloseDialogWindow(applyButton, true);
        };
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => CloseDialogWindow(cancelButton, false);

        var content = CreateDialogCard(
            title,
            new StackPanel
            {
                Children =
                {
                    CreateFieldLabel(label),
                    amountBox,
                    errorText
                }
            },
            cancelButton,
            applyButton);

        var result = await ShowModalDialogAsync(content, title);
        return result is true ? amount : null;
    }

    public virtual IciciExportOptions? AskIciciExportOptions(string? savedDebitAccount)
    {
        var app = Application.Current;
        if (app?.Dispatcher.CheckAccess() == false)
        {
            return app.Dispatcher.Invoke(() => AskIciciExportOptions(savedDebitAccount));
        }

        return ShowIciciExportOptionsDialog(savedDebitAccount);
    }

    public virtual async Task<IciciExportOptions?> AskIciciExportOptionsAsync(string? savedDebitAccount)
    {
        var app = Application.Current;
        if (app is null)
            return AskIciciExportOptions(savedDebitAccount);

        if (!app.Dispatcher.CheckAccess())
        {
            var operation = app.Dispatcher.InvokeAsync(() => AskIciciExportOptionsAsync(savedDebitAccount));
            return await await operation.Task;
        }

        return ShowIciciExportOptionsDialog(savedDebitAccount);
    }

    public virtual Task<bool> ShowEmployeeDialogAsync(AddEmployeeViewModel vm, Window? owner = null)
        => ShowContentDialogAsync(new AddEmployeeWindow(vm), owner);

    public virtual Task<bool> ShowAdvanceEntryDialogAsync(AdvanceEntryEditVm vm, Window? owner = null)
        => ShowContentDialogAsync(new AdvanceEntryWindow(vm), owner);

    private static IciciExportOptions? ShowIciciExportOptionsDialog(string? savedDebitAccount)
    {
        IciciExportOptions? options = null;
        var accountBox = new TextBox
        {
            Text = savedDebitAccount ?? string.Empty,
            MinWidth = 280,
            Margin = new Thickness(0, 4, 0, 12)
        };
        var datePicker = new DatePicker
        {
            SelectedDate = DateTime.Today,
            Margin = new Thickness(0, 4, 0, 8)
        };
        var errorText = new TextBlock
        {
            Foreground = FindBrush("DangerBrush", Brushes.Firebrick),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var exportButton = CreateButton("Export", true, false);
        var cancelButton = CreateButton("Cancel", false, false);
        cancelButton.IsCancel = true;
        exportButton.Click += (_, _) =>
        {
            var debitAccount = accountBox.Text.Trim();
            if (debitAccount.Length is < 6 or > 30 || !debitAccount.All(char.IsDigit))
            {
                errorText.Text = "Debit account number must contain 6 to 30 digits.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            if (datePicker.SelectedDate is not DateTime paymentDate)
            {
                errorText.Text = "Payment date is required.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            options = new IciciExportOptions(debitAccount, paymentDate.Date);
            CloseDialogWindow(exportButton, true);
        };
        cancelButton.Click += (_, _) => CloseDialogWindow(cancelButton, false);

        var content = CreateDialogCard(
            "ICICI Export Options",
            new StackPanel
            {
                Children =
                {
                    CreateFieldLabel("ICICI debit account"),
                    accountBox,
                    CreateFieldLabel("Payment date"),
                    datePicker,
                    errorText
                }
            },
            cancelButton,
            exportButton);

        var result = ShowModalDialog(content, "ICICI Export Options");
        return result is true ? options : null;
    }

    private async Task<bool> ShowContentDialogAsync(FrameworkElement content, Window? owner)
    {
        var app = Application.Current;
        if (app is null)
            return false;

        if (!app.Dispatcher.CheckAccess())
        {
            var operation = app.Dispatcher.InvokeAsync(() => ShowContentDialogAsync(content, owner));
            return await await operation.Task;
        }

        return ShowModalDialog(content, ResolveContentDialogTitle(content), owner) == true;
    }

    private async Task<bool> ShowChoiceAsync(
        string message,
        string title,
        string cancelText,
        string confirmText,
        bool isDestructive,
        bool useWarningFallback = false)
    {
        var app = Application.Current;
        if (app is null)
            return isDestructive
                ? ConfirmDestructive(message, title)
                : useWarningFallback
                    ? ConfirmWarning(message, title)
                    : Confirm(message, title);

        if (!app.Dispatcher.CheckAccess())
        {
            var operation = app.Dispatcher.InvokeAsync(() => ShowChoiceAsync(message, title, cancelText, confirmText, isDestructive, useWarningFallback));
            return await await operation.Task;
        }

        var cancelButton = CreateButton(cancelText, false, false);
        var confirmButton = CreateButton(confirmText, true, isDestructive);
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => CloseDialogWindow(cancelButton, false);
        confirmButton.Click += (_, _) => CloseDialogWindow(confirmButton, true);

        var result = await ShowModalDialogAsync(CreateDialogCard(title, CreateMessage(message), cancelButton, confirmButton), title);
        return result is true;
    }

    private async Task ShowNoticeAsync(string message, string title, string closeText, bool isError)
    {
        var app = Application.Current;
        if (app is null)
        {
            if (isError) Error(message, title); else Info(message, title);
            return;
        }

        if (!app.Dispatcher.CheckAccess())
        {
            var operation = app.Dispatcher.InvokeAsync(() => ShowNoticeAsync(message, title, closeText, isError));
            await operation.Task.Unwrap();
            return;
        }

        var closeButton = CreateButton(closeText, true, false);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => CloseDialogWindow(closeButton, true);

        await ShowModalDialogAsync(CreateDialogCard(title, CreateMessage(message), closeButton), title);
    }

    private static async Task<bool?> ShowModalDialogAsync(UIElement content, string title, Window? owner = null)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            var operation = Application.Current.Dispatcher.InvokeAsync(() => ShowModalDialog(content, title, owner));
            return await operation.Task;
        }

        return ShowModalDialog(content, title, owner);
    }

    private static bool? ShowModalDialog(UIElement content, string title, Window? owner = null)
    {
        var dialogOwner = ResolveOwner(owner);
        var window = new Window
        {
            Title = title,
            Content = CreateSurface(content),
            Owner = dialogOwner,
            WindowStartupLocation = dialogOwner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = FindBrush("BackgroundBrush", Brushes.White),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        return window.ShowDialog();
    }

    private static Window? ResolveOwner(Window? requestedOwner)
    {
        if (requestedOwner?.IsVisible == true)
            return requestedOwner;

        var app = Application.Current;
        return app?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? (app?.MainWindow?.IsVisible == true ? app.MainWindow : null);
    }

    private static void CloseDialogWindow(FrameworkElement source, bool result)
    {
        if (Window.GetWindow(source) is { } window)
            window.DialogResult = result;
    }

    private static Border CreateSurface(UIElement content)
        => new()
        {
            Background = FindBrush("BackgroundBrush", Brushes.White),
            CornerRadius = new CornerRadius(8),
            Child = content
        };

    private static Border CreateDialogCard(string title, UIElement body, params Button[] buttons)
    {
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(8, 0, 0, 0);
            buttonPanel.Children.Add(button);
        }

        return new Border
        {
            Width = 420,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(8),
            Background = FindBrush("SurfaceBrush", Brushes.White),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FindBrush("TextBrush", Brushes.Black),
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    body,
                    buttonPanel
                }
            }
        };
    }

    private static TextBlock CreateMessage(string message)
        => new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FindBrush("TextBrush", Brushes.Black)
        };

    private static TextBlock CreateFieldLabel(string text)
        => new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("MutedTextBrush", Brushes.DimGray),
            Margin = new Thickness(0, 0, 0, 4)
        };

    private static Button CreateButton(string text, bool isPrimary, bool isDestructive)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            IsDefault = isPrimary
        };
        var styleKey = isDestructive ? "DangerButton" : isPrimary ? "PrimaryButton" : "SecondaryButton";
        if (Application.Current?.TryFindResource(styleKey) is Style style)
            button.Style = style;
        return button;
    }

    private static Brush FindBrush(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static string ResolveContentDialogTitle(FrameworkElement content)
        => content.DataContext switch
        {
            AddEmployeeViewModel employeeVm => employeeVm.WindowTitle,
            AdvanceEntryEditVm => "Edit advance entry",
            _ => "Salary Manager"
        };
}
