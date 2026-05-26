using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.Data;
using Xunit;

namespace SalaryManager.Tests;

public class AsyncViewModelLoadTests
{
    [Fact]
    public async Task AttendanceLoad_StaleFailureIsIgnoredWhenNewerLoadCompletes()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var dialogs = new CapturingDialogService();
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

        Assert.Empty(dialogs.Errors);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public async Task SalarySheetLoad_RealErrorClearsLoadingAndSurfacesOnce()
    {
        using var factory = new SequencedDbContextFactory();
        var initializer = await ReadyInitializerAsync(factory);
        var dialogs = new CapturingDialogService();
        var viewModel = new SalarySheetViewModel(
            factory,
            initializer,
            new PdfSlipService(),
            new ExcelExportService(),
            dialogs,
            new AppSettingsService());

        factory.EnqueueAsync(_ => Task.FromException<AppDbContext>(new InvalidOperationException("database unavailable")));

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoading);
        Assert.Equal(["database unavailable"], dialogs.Errors);
        Assert.Empty(viewModel.Rows);
    }

    private static async Task<DatabaseInitializer> ReadyInitializerAsync(IDbContextFactory<AppDbContext> factory)
    {
        var initializer = new DatabaseInitializer();
        initializer.StartMigration(factory);
        await initializer.ReadyTask.WaitAsync(TimeSpan.FromSeconds(10));
        return initializer;
    }

    private sealed class CapturingDialogService : DialogService
    {
        public List<string> Errors { get; } = [];

        public override void Error(string message, string title = "Error")
            => Errors.Add(message);
    }

    private sealed class SequencedDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<Func<CancellationToken, Task<AppDbContext>>> _asyncContexts = new();
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

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
