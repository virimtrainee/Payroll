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
    public void AppPaths_DatabasePathCanBeOverriddenForMockRuns()
    {
        var original = Environment.GetEnvironmentVariable(AppPaths.DatabasePathEnvironmentVariable);
        var overridePath = Path.Combine(Path.GetTempPath(), "SalaryManagerTests", Guid.NewGuid().ToString("N"), "mock.db");

        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DatabasePathEnvironmentVariable, overridePath);

            Assert.Equal(Path.GetFullPath(overridePath), AppPaths.DatabasePath);
            Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(overridePath)), AppPaths.DatabaseDirectory);
            Assert.StartsWith(AppPaths.DataDirectory, AppPaths.SettingsPath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(AppPaths.DataDirectory, AppPaths.SlipsDirectory, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DatabasePathEnvironmentVariable, original);
        }
    }

    [Fact]
    public void DesignTimeFactory_CreatesContextWithoutRuntimeDatabasePath()
    {
        using var db = new AppDbContextDesignTimeFactory().CreateDbContext([]);

        Assert.NotNull(db);
    }

    [Fact]
    public void AppSettingsService_LoadsOldSettingsAndSavesSalaryColumnWidths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SalaryManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");

        try
        {
            File.WriteAllText(path, """{"IciciDebitAccountNo":"1234567890"}""");
            var service = new AppSettingsService(path);

            var oldSettings = service.Load();

            Assert.Equal("1234567890", oldSettings.IciciDebitAccountNo);
            Assert.NotNull(oldSettings.SalarySheetColumnWidths);
            Assert.Empty(oldSettings.SalarySheetColumnWidths);

            service.Save(oldSettings with
            {
                SalarySheetColumnWidths = new Dictionary<string, double>
                {
                    ["employee"] = 260,
                    ["netSalary"] = 150
                }
            });

            var saved = service.Load();

            Assert.Equal(260, saved.SalarySheetColumnWidths!["employee"]);
            Assert.Equal(150, saved.SalarySheetColumnWidths["netSalary"]);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
