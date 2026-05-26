using SalaryManager.App.Services;
using SalaryManager.Data;
using Xunit;

namespace SalaryManager.Tests;

public class AppRuntimeStorageTests
{
    [Fact]
    public void AppPaths_StoresRuntimeFilesBesideApplication()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(AppPaths.ApplicationDirectory, AppPaths.DataDirectory);
        Assert.DoesNotContain(localAppData, AppPaths.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.ApplicationDirectory, AppPaths.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.SlipsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.SettingsPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesignTimeFactory_CreatesContextWithoutRuntimeDatabasePath()
    {
        using var db = new AppDbContextDesignTimeFactory().CreateDbContext([]);

        Assert.NotNull(db);
    }
}
