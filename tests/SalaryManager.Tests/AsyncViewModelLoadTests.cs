using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.Data;
using Xunit;

namespace SalaryManager.Tests;

public class AsyncViewModelLoadTests
{
    [Fact]
    public async Task EmployeesConstructor_DoesNotStartLoadBeforeExplicitCommand()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var dialogs = NewDialogService();

        _ = new EmployeesViewModel(factory, initializer, dialogs, new ExcelImportService());
        await Task.Delay(100);

        Assert.Equal(0, factory.AsyncCreateCount);
    }

    [Fact]
    public async Task OpenAddEmployee_ShowsDialogBeforeGroupOptionsFinishLoading()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var groupLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGroupLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new CapturingEmployeeDialogService();
        var viewModel = new EmployeesViewModel(factory, initializer, dialogs, new ExcelImportService());

        factory.EnqueueAsync(async ct =>
        {
            groupLoadStarted.SetResult();
            await releaseGroupLoad.Task.WaitAsync(ct);
            return factory.CreateDbContext();
        });

        var addTask = viewModel.OpenAddEmployeeCommand.ExecuteAsync(null);

        await dialogs.EmployeeDialogShown.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(groupLoadStarted.Task.IsCompleted);
        Assert.False(releaseGroupLoad.Task.IsCompleted);

        releaseGroupLoad.SetResult();
        await addTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AttendanceLoad_StaleFailureIsIgnoredWhenNewerLoadCompletes()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var dialogs = NewDialogService();
        var viewModel = new AttendanceViewModel(factory, initializer, dialogs);

        factory.EnqueueAsync(async ct =>
        {
            await Task.Delay(100, ct);
            throw new InvalidOperationException("stale load failed");
        });
        factory.EnqueueAsync(_ => Task.FromResult(factory.CreateDbContext()));

        var staleLoad = viewModel.LoadCommand.ExecuteAsync(null);
        await Task.Delay(10);
        var latestLoad = viewModel.LoadCommand.ExecuteAsync(null);

        await latestLoad;
        await staleLoad;

        _ = dialogs.DidNotReceive().ErrorAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public async Task SalarySheetLoad_RealErrorClearsLoadingAndSurfacesOnce()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var dialogs = NewDialogService();
        var viewModel = new SalarySheetViewModel(
            factory,
            initializer,
            new MonthlyPayrollService(factory),
            new PdfSlipService(),
            new ExcelExportService(),
            dialogs,
            new AppSettingsService());

        factory.EnqueueAsync(_ => Task.FromException<AppDbContext>(new InvalidOperationException("database unavailable")));

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoading);
        _ = dialogs.Received(1).ErrorAsync("database unavailable", "Error");
        Assert.Empty(viewModel.Rows);
    }

    private static DialogService NewDialogService()
    {
        var dialogs = Substitute.For<DialogService>();
        dialogs.ErrorAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        return dialogs;
    }

    private sealed class CapturingEmployeeDialogService : DialogService
    {
        public TaskCompletionSource EmployeeDialogShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<bool> ShowEmployeeDialogAsync(AddEmployeeViewModel vm, Window? owner = null)
        {
            EmployeeDialogShown.SetResult();
            return Task.FromResult(false);
        }
    }

    private static async Task<DatabaseInitializer> ReadyInitializerAsync(IDbContextFactory<AppDbContext> factory)
    {
        var initializer = new DatabaseInitializer();
        initializer.StartMigration(factory);
        await initializer.ReadyTask.WaitAsync(TimeSpan.FromSeconds(10));
        return initializer;
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
