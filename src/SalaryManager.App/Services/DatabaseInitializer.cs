using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data;

namespace SalaryManager.App.Services;

public class DatabaseInitializer
{
    private readonly TaskCompletionSource _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ReadyTask => _tcs.Task;

    public void StartMigration(IDbContextFactory<AppDbContext> factory)
    {
        _ = Task.Run(() =>
        {
            try
            {
                PrepareRuntimeData();
                using var db = factory.CreateDbContext();
                db.Database.Migrate();
                _tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        });
    }

    public static void PrepareRuntimeData()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Directory.CreateDirectory(AppPaths.SlipsDirectory);
    }
}
