using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.App.Views;
using SalaryManager.Data;
using Syncfusion.Licensing;

namespace SalaryManager.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = true;
        var syncfusionLicenseKey =
            SyncfusionLicenseKey.Value
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(syncfusionLicenseKey))
            SyncfusionLicenseProvider.RegisterLicense(syncfusionLicenseKey);

        // Enter key moves focus to next control — but NOT inside DataGrid cells
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.KeyDownEvent,
            new KeyEventHandler(TextBox_KeyDown));

        // Select all text when a TextBox receives focus
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(TextBox_GotKeyboardFocus));

        var connStr = new SqliteConnectionStringBuilder { DataSource = AppPaths.DatabasePath }.ToString();

        var sc = new ServiceCollection();

        sc.AddSingleton<SqlitePragmaConnectionInterceptor>();
        sc.AddPooledDbContextFactory<AppDbContext>((sp, opts) =>
            opts.UseSqlite(connStr)
                .AddInterceptors(sp.GetRequiredService<SqlitePragmaConnectionInterceptor>()));

        sc.AddSingleton<DatabaseInitializer>();
        sc.AddSingleton<PdfSlipService>();
        sc.AddSingleton<ExcelExportService>();
        sc.AddSingleton<ExcelImportService>();
        sc.AddSingleton<MonthlyPayrollService>();
        sc.AddSingleton<DialogService>();
        sc.AddSingleton<BackupService>();
        sc.AddSingleton<AppSettingsService>();

        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<DashboardViewModel>();
        sc.AddTransient<EmployeesViewModel>();
        sc.AddTransient<AttendanceViewModel>();
        sc.AddTransient<AdvancesViewModel>();
        sc.AddTransient<SalarySheetViewModel>();
        sc.AddTransient<ReportsViewModel>();

        sc.AddSingleton<MainWindow>();
        sc.AddTransient<EmployeesWindow>();

        Services = sc.BuildServiceProvider();

        base.OnStartup(e);

        var window = Services.GetRequiredService<MainWindow>();
        window.DataContext = Services.GetRequiredService<MainViewModel>();
        window.Show();

        // Run migration in the background; VMs wait on DatabaseInitializer.ReadyTask
        // before loading data, so the window paints immediately.
        var dbInit = Services.GetRequiredService<DatabaseInitializer>();
        var dbFactory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        dbInit.StartMigration(dbFactory);
    }

    private static void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key != Key.Enter) return;
        if (sender is not TextBox tb) return;
        if (tb.AcceptsReturn) return;

        // Let the DataGrid handle Enter natively (commit + next row)
        if (IsInsideDataGrid(tb)) return;

        var request = new TraversalRequest(
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                ? FocusNavigationDirection.Previous
                : FocusNavigationDirection.Next);
        tb.MoveFocus(request);
        e.Handled = true;
    }

    private static bool IsInsideDataGrid(DependencyObject element)
    {
        var parent = VisualTreeHelper.GetParent(element);
        while (parent != null)
        {
            if (parent is DataGrid or Syncfusion.UI.Xaml.Grid.SfDataGrid) return true;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }

    private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }
}
