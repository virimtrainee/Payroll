using System;
using System.IO;

namespace SalaryManager.App.Services;

public static class AppPaths
{
    public const string DatabasePathEnvironmentVariable = "SALARYMANAGER_DB_PATH";

    public static string ApplicationDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath)
                && !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var processDir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(processDir))
                    return processDir;
            }

            return AppContext.BaseDirectory;
        }
    }

    public static string DataDirectory => ApplicationDirectory;

    public static string DatabasePath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridePath));

            return Path.Combine(DataDirectory, "salary.db");
        }
    }

    public static string DatabaseDirectory
        => Path.GetDirectoryName(DatabasePath) ?? DataDirectory;

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string SlipsDirectory => Path.Combine(DataDirectory, "Slips");
}
