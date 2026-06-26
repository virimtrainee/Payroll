using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SalaryManager.App.Services;

public record AppSettings(
    string? IciciDebitAccountNo,
    Dictionary<string, double>? SalarySheetColumnWidths = null,
    string? FirmName = null);

public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly string _dataDirectory;
    private readonly object _gate = new();
    private AppSettings? _cached;

    public AppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsPath;
        _dataDirectory = Path.GetDirectoryName(_settingsPath) ?? AppPaths.DataDirectory;
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (_cached is not null)
                return _cached;
        }

        var settings = LoadFromDisk();
        lock (_gate)
            _cached = settings;

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var normalized = Normalize(settings);
        Directory.CreateDirectory(_dataDirectory);
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(tempPath, _settingsPath, overwrite: true);

        lock (_gate)
            _cached = normalized;
    }

    private AppSettings LoadFromDisk()
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

        var firmName = string.IsNullOrWhiteSpace(settings.FirmName)
            ? null
            : settings.FirmName.Trim();

        return settings with
        {
            SalarySheetColumnWidths = widths,
            FirmName = firmName
        };
    }
}
