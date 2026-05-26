using System;
using System.IO;

namespace SalaryManager.App.Services;

public static class AppPaths
{
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

    public static string DatabasePath => Path.Combine(ApplicationDirectory, "salary.db");

    public static string SlipsDirectory => Path.Combine(ApplicationDirectory, "Slips");
}
