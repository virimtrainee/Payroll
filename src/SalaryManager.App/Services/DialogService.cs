using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using SalaryManager.App.ViewModels;

namespace SalaryManager.App.Services;

public class DialogService
{
    public const string RootDialogHostIdentifier = "RootDialogHost";

    public virtual bool Confirm(string message, string title = "Confirm")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public virtual bool ConfirmDestructive(string message, string title = "Confirm Delete")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public virtual void Info(string message, string title = "Information")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public virtual void Error(string message, string title = "Error")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public virtual Task<bool> ConfirmAsync(string message, string title = "Confirm")
        => ShowChoiceAsync(message, title, "No", "Yes", false);

    public virtual Task<bool> ConfirmDestructiveAsync(string message, string title = "Confirm Delete")
        => ShowChoiceAsync(message, title, "Cancel", "Delete", true);

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

    public virtual IciciExportOptions? AskIciciExportOptions(string? savedDebitAccount)
    {
        var accountBox = new TextBox
        {
            Text = savedDebitAccount ?? string.Empty,
            MinWidth = 220,
            Margin = new Thickness(0, 4, 0, 12)
        };
        var datePicker = new DatePicker
        {
            SelectedDate = DateTime.Today,
            Margin = new Thickness(0, 4, 0, 18)
        };

        var saveButton = new Button
        {
            Content = "Export",
            IsDefault = true,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 84
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "ICICI debit account", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(accountBox);
        panel.Children.Add(new TextBlock { Text = "Payment date", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(datePicker);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "ICICI Export Options",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        IciciExportOptions? options = null;
        saveButton.Click += (_, _) =>
        {
            var debitAccount = accountBox.Text.Trim();
            if (debitAccount.Length is < 6 or > 30 || !debitAccount.All(char.IsDigit))
            {
                Error("Debit account number must contain 6 to 30 digits.", "Invalid ICICI export options");
                return;
            }

            if (datePicker.SelectedDate is not DateTime paymentDate)
            {
                Error("Payment date is required.", "Invalid ICICI export options");
                return;
            }

            options = new IciciExportOptions(debitAccount, paymentDate.Date);
            window.DialogResult = true;
        };

        return window.ShowDialog() == true ? options : null;
    }

    public virtual async Task<IciciExportOptions?> AskIciciExportOptionsAsync(string? savedDebitAccount)
    {
        if (Application.Current is null)
            return AskIciciExportOptions(savedDebitAccount);

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
            DialogHost.CloseDialogCommand.Execute(true, exportButton);
        };
        cancelButton.Click += (_, _) => DialogHost.CloseDialogCommand.Execute(false, cancelButton);

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

        var result = await ShowDialogHostAsync(content);
        return result is true ? options : null;
    }

    public virtual Task<bool> ShowEmployeeDialogAsync(AddEmployeeViewModel vm, Window? owner = null)
        => ShowContentDialogAsync(new AddEmployeeWindow(vm), owner);

    public virtual Task<bool> ShowAdvanceEntryDialogAsync(AdvanceEntryEditVm vm, Window? owner = null)
        => ShowContentDialogAsync(new AdvanceEntryWindow(vm), owner);

    private async Task<bool> ShowContentDialogAsync(FrameworkElement content, Window? owner)
    {
        if (Application.Current is null)
            return false;

        _ = owner;
        var result = await ShowDialogHostAsync(CreateSurface(content));
        return result is true;
    }

    private async Task<bool> ShowChoiceAsync(string message, string title, string cancelText, string confirmText, bool isDestructive)
    {
        if (Application.Current is null)
            return isDestructive ? ConfirmDestructive(message, title) : Confirm(message, title);

        var cancelButton = CreateButton(cancelText, false, false);
        var confirmButton = CreateButton(confirmText, true, isDestructive);
        cancelButton.Click += (_, _) => DialogHost.CloseDialogCommand.Execute(false, cancelButton);
        confirmButton.Click += (_, _) => DialogHost.CloseDialogCommand.Execute(true, confirmButton);

        var result = await ShowDialogHostAsync(CreateDialogCard(title, CreateMessage(message), cancelButton, confirmButton));
        return result is true;
    }

    private async Task ShowNoticeAsync(string message, string title, string closeText, bool isError)
    {
        if (Application.Current is null)
        {
            if (isError) Error(message, title); else Info(message, title);
            return;
        }

        var closeButton = CreateButton(closeText, true, false);
        closeButton.Click += (_, _) => DialogHost.CloseDialogCommand.Execute(true, closeButton);

        await ShowDialogHostAsync(CreateDialogCard(title, CreateMessage(message), closeButton));
    }

    private static async Task<object?> ShowDialogHostAsync(object content)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            var operation = Application.Current.Dispatcher.InvokeAsync(() => ShowDialogHostAsync(content));
            return await await operation.Task;
        }

        return await DialogHost.Show(content, RootDialogHostIdentifier);
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
}
