using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.Data;
using SalaryManager.Data.Entities;

namespace SalaryManager.Tests;

public class MainViewModelReloadTests
{
    [Fact]
    public async Task EmployeeDelete_MarksPayrollViewsDirtyAndReloadsSelectedTabLazily()
    {
        using var factory = new SqliteDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Delete Me", BaseSalary = 10000m, IsActive = true },
                new Employee { Id = 2, Name = "Keep", BaseSalary = 12000m, IsActive = true });
        });

        var main = NewMainViewModel(factory, initializer);
        await main.SalarySheetVm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(["Delete Me", "Keep"], main.SalarySheetVm.Rows.Select(r => r.Name));

        main.SelectedPageIndex = 4;
        await WaitForLatestExecutionAsync(main.EmployeesVm.LoadCommand);
        var employee = main.EmployeesVm.Employees.Single(e => e.Name == "Delete Me");

        await main.EmployeesVm.DeleteEmployeeCommand.ExecuteAsync(employee);

        Assert.Equal(["Delete Me", "Keep"], main.SalarySheetVm.Rows.Select(r => r.Name));

        main.SelectedPageIndex = 1;
        await WaitForLatestExecutionAsync(main.SalarySheetVm.LoadCommand);

        Assert.Equal(["Keep"], main.SalarySheetVm.Rows.Select(r => r.Name));
    }

    private static MainViewModel NewMainViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
    {
        var dialogs = NewDialogService();
        var settings = new AppSettingsService();
        var monthlyPayroll = new MonthlyPayrollService(factory);
        var dashboard = new DashboardViewModel(factory, initializer, new BackupService(), dialogs, monthlyPayroll);
        var salarySheet = new SalarySheetViewModel(
            factory,
            initializer,
            monthlyPayroll,
            new PdfSlipService(),
            new ExcelExportService(),
            dialogs,
            settings)
        {
            SelectedYear = 2026,
            SelectedMonth = SalaryManager.App.Helpers.Months.All.Single(m => m.Number == 5)
        };
        var advances = new AdvancesViewModel(factory, initializer, dialogs);
        var reports = new ReportsViewModel(
            factory,
            initializer,
            new PdfSlipService(),
            new ExcelExportService(),
            monthlyPayroll,
            dialogs,
            settings);
        var employees = new EmployeesViewModel(factory, initializer, dialogs, new ExcelImportService());

        var services = new Dictionary<Type, object>
        {
            [typeof(DashboardViewModel)] = dashboard,
            [typeof(SalarySheetViewModel)] = salarySheet,
            [typeof(AdvancesViewModel)] = advances,
            [typeof(ReportsViewModel)] = reports,
            [typeof(EmployeesViewModel)] = employees
        };

        return new MainViewModel(new TestServiceProvider(services));
    }

    private static async Task WaitForLatestExecutionAsync(IAsyncRelayCommand command)
    {
        var execution = command.ExecutionTask;
        if (execution is not null)
            await execution.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static DialogService NewDialogService()
    {
        var dialogs = Substitute.For<DialogService>();
        dialogs.ErrorAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        dialogs.InfoAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        dialogs.ConfirmDestructiveAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult(true));
        return dialogs;
    }

    private static async Task SeedAsync(SqliteDbContextFactory factory, Action<AppDbContext> seed)
    {
        using var db = factory.CreateDbContext();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static async Task<DatabaseInitializer> ReadyInitializerAsync(IDbContextFactory<AppDbContext> factory)
    {
        var initializer = new DatabaseInitializer();
        initializer.StartMigration(factory);
        await initializer.ReadyTask.WaitAsync(TimeSpan.FromSeconds(10));
        return initializer;
    }

    private sealed class TestServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => services.TryGetValue(serviceType, out var service) ? service : null;
    }

    private sealed class SqliteDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public SqliteDbContextFactory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public AppDbContext CreateDbContext()
            => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());

        public void Dispose()
            => _connection.Dispose();
    }
}
