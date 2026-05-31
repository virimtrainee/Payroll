using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;
using Xunit;

namespace SalaryManager.Tests;

public class ViewModelDebounceTests
{
    [Fact]
    public async Task EmployeesSearch_DebouncesReloads()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Alice", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 2, Name = "Alicia", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 3, Name = "Bob", BaseSalary = 1000m, IsActive = true });
        });

        var viewModel = NewEmployeesViewModel(factory, initializer);

        viewModel.SearchText = "A";
        viewModel.SearchText = "Al";
        viewModel.SearchText = "Ali";

        await WaitUntilAsync(() => viewModel.Employees.Count == 2);
        await Task.Delay(EmployeesViewModel.SearchReloadDebounceDelay + TimeSpan.FromMilliseconds(80));

        Assert.Equal(1, factory.AsyncCreateCount);
        Assert.Equal(["Alice", "Alicia"], viewModel.Employees.Select(e => e.Name));
    }

    [Fact]
    public async Task EmployeesSearch_ExplicitLoadCancelsPendingDebounce()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Alice", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 2, Name = "Bob", BaseSalary = 1000m, IsActive = true });
        });

        var viewModel = NewEmployeesViewModel(factory, initializer);

        viewModel.SearchText = "Ali";
        await viewModel.LoadCommand.ExecuteAsync(null);
        await Task.Delay(EmployeesViewModel.SearchReloadDebounceDelay + TimeSpan.FromMilliseconds(80));

        Assert.Equal(1, factory.AsyncCreateCount);
        Assert.Equal(["Alice"], viewModel.Employees.Select(e => e.Name));
    }

    [Fact]
    public async Task EmployeesLoad_StaleCancelledLoadDoesNotShowError()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var dialogs = NewDialogService();
        var viewModel = new EmployeesViewModel(factory, initializer, dialogs, new ExcelImportService());
        var staleLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        factory.EnqueueAsync(async ct =>
        {
            staleLoadStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            throw new InvalidOperationException("stale load failed");
        });
        factory.EnqueueAsync(_ => Task.FromResult(factory.CreateDbContext()));

        var staleLoad = viewModel.LoadCommand.ExecuteAsync(null);
        await staleLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var latestLoad = viewModel.LoadCommand.ExecuteAsync(null);

        await latestLoad.WaitAsync(TimeSpan.FromSeconds(10));
        await staleLoad.WaitAsync(TimeSpan.FromSeconds(10));

        _ = dialogs.DidNotReceive().ErrorAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AdvancesSearch_DebouncesFilterRefresh()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAdvanceEmployeesAsync(factory);

        var viewModel = new AdvancesViewModel(factory, initializer, NewDialogService());
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.FilteredCount);

        viewModel.SearchText = "A";
        viewModel.SearchText = "Al";
        viewModel.SearchText = "Ali";

        Assert.Equal(3, viewModel.FilteredCount);

        await WaitUntilAsync(() => viewModel.FilteredCount == 2);

        Assert.Equal(["Alice", "Alicia"], viewModel.EmployeeRailView.Cast<EmployeeWithBalanceVm>().Select(e => e.Name));
    }

    [Fact]
    public async Task AdvancesLoad_CancelsPendingFilterRefreshAndAppliesCurrentFilter()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        await SeedAdvanceEmployeesAsync(factory);

        var viewModel = new AdvancesViewModel(factory, initializer, NewDialogService());
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "Bob";
        await viewModel.LoadCommand.ExecuteAsync(null);
        await Task.Delay(AdvancesViewModel.SearchFilterDebounceDelay + TimeSpan.FromMilliseconds(80));

        Assert.Equal(1, viewModel.FilteredCount);
        Assert.Equal(["Bob"], viewModel.EmployeeRailView.Cast<EmployeeWithBalanceVm>().Select(e => e.Name));
    }

    private static EmployeesViewModel NewEmployeesViewModel(
        IDbContextFactory<AppDbContext> factory,
        DatabaseInitializer initializer)
        => new(factory, initializer, NewDialogService(), new ExcelImportService());

    private static DialogService NewDialogService()
    {
        var dialogs = Substitute.For<DialogService>();
        dialogs.ErrorAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        dialogs.InfoAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        return dialogs;
    }

    private static async Task<DatabaseInitializer> ReadyInitializerAsync(IDbContextFactory<AppDbContext> factory)
    {
        var initializer = new DatabaseInitializer();
        initializer.StartMigration(factory);
        await initializer.ReadyTask.WaitAsync(TimeSpan.FromSeconds(10));
        return initializer;
    }

    private static async Task SeedAsync(SequencedDbContextFactory factory, Action<AppDbContext> seed)
    {
        using var db = factory.CreateDbContext();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdvanceEmployeesAsync(SequencedDbContextFactory factory)
    {
        await SeedAsync(factory, db =>
        {
            db.Employees.AddRange(
                new Employee { Id = 1, Name = "Alice", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 2, Name = "Alicia", BaseSalary = 1000m, IsActive = true },
                new Employee { Id = 3, Name = "Bob", BaseSalary = 1000m, IsActive = true });
            db.Advances.AddRange(
                new Advance { EmployeeId = 1, Date = new DateTime(2026, 5, 1), EntryType = AdvanceEntryType.Given, Amount = 100m },
                new Advance { EmployeeId = 2, Date = new DateTime(2026, 5, 1), EntryType = AdvanceEntryType.Given, Amount = 100m },
                new Advance { EmployeeId = 3, Date = new DateTime(2026, 5, 1), EntryType = AdvanceEntryType.Given, Amount = 100m });
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(20, cts.Token);
        }
    }

    private sealed class SequencedDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<Func<CancellationToken, Task<AppDbContext>>> _asyncContexts = new();
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private int _asyncCreateCount;

        public int AsyncCreateCount => Volatile.Read(ref _asyncCreateCount);

        public SequencedDbContextFactory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public void EnqueueAsync(Func<CancellationToken, Task<AppDbContext>> factory)
        {
            lock (_gate)
                _asyncContexts.Enqueue(factory);
        }

        public AppDbContext CreateDbContext()
            => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asyncCreateCount);
            Func<CancellationToken, Task<AppDbContext>>? next = null;
            lock (_gate)
            {
                if (_asyncContexts.Count > 0)
                    next = _asyncContexts.Dequeue();
            }

            return next is null
                ? Task.FromResult(CreateDbContext())
                : next(cancellationToken);
        }

        public void Dispose()
            => _connection.Dispose();
    }
}
