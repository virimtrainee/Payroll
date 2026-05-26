using System;
using System.IO;
using System.Text.Json;

namespace SalaryManager.App.Services;

public record AppSettings(string? IciciDebitAccountNo);

public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        if (!File.Exists(AppPaths.SettingsPath))
            return new AppSettings(null);

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings(null);
        }
        catch
        {
            return new AppSettings(null);
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var tempPath = AppPaths.SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, AppPaths.SettingsPath, overwrite: true);
    }
}
