using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SalaryManager.App.Services;

public class DialogService
{
    public virtual bool Confirm(string message, string title = "Confirm")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public virtual bool ConfirmDestructive(string message, string title = "Confirm Delete")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public virtual void Info(string message, string title = "Information")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public virtual void Error(string message, string title = "Error")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

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
}
