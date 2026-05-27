using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SalaryManager.App.Services;

public record AppSettings(
    string? IciciDebitAccountNo,
    Dictionary<string, double>? SalarySheetColumnWidths = null);

public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly string _dataDirectory;

    public AppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsPath;
        _dataDirectory = Path.GetDirectoryName(_settingsPath) ?? AppPaths.DataDirectory;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return Empty();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json));
        }
        catch
        {
            return Empty();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_dataDirectory);
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    private static AppSettings Empty() => new(null, new Dictionary<string, double>());

    private static AppSettings Normalize(AppSettings? settings)
    {
        if (settings is null)
            return Empty();

        var widths = settings.SalarySheetColumnWidths?
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key)
                          && double.IsFinite(kvp.Value)
                          && kvp.Value > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, double>();

        return settings with { SalarySheetColumnWidths = widths };
    }
}
