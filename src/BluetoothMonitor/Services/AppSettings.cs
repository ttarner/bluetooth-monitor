using System.IO;
using System.Text.Json;
using BluetoothMonitor.Models;

namespace BluetoothMonitor.Services;

public sealed class AppSettings
{
    public const int DefaultLowBatteryThreshold = 20;
    public const int MinimumLowBatteryThreshold = 1;
    public const int MaximumLowBatteryThreshold = 50;
    public const string DefaultWeatherLocation = "Rome, Italy";
    public const int DefaultWeatherRefreshMinutes = 15;
    public const int MinimumWeatherRefreshMinutes = 5;
    public const int MaximumWeatherRefreshMinutes = 120;
    public const int DefaultTemperatureRefreshSeconds = 5;
    public const int MinimumTemperatureRefreshSeconds = 1;
    public const int MaximumTemperatureRefreshSeconds = 60;

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothMonitor");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.TopRight;
    public bool StartWithWindows { get; set; }
    public bool ShowOverlayOnStartup { get; set; } = true;
    public bool KeepRunningInTrayOnClose { get; set; }
    public double OverlayBackgroundOpacity { get; set; } = 0.78d;
    public double OverlayTextOpacity { get; set; } = 1d;
    public double OverlayScale { get; set; } = 1d;
    public bool ShowClockInOverlay { get; set; }
    public bool ShowCpuInOverlay { get; set; }
    public bool ShowCpuTemperatureInOverlay { get; set; }
    public bool ShowGpuTemperatureInOverlay { get; set; }
    public bool ShowRamInOverlay { get; set; }
    public bool ShowNetworkInOverlay { get; set; }
    public bool ShowNetworkOutOverlay { get; set; }
    public TemperatureDataSourceMode TemperatureDataSource { get; set; } = TemperatureDataSourceMode.Auto;
    public int TemperatureRefreshSeconds { get; set; } = DefaultTemperatureRefreshSeconds;
    public bool ShowWeatherInOverlay { get; set; } = true;
    public bool ShowNowPlayingInOverlay { get; set; } = true;
    public bool ShowAnimeInfoInOverlay { get; set; } = true;
    public string WeatherLocation { get; set; } = DefaultWeatherLocation;
    public int WeatherRefreshMinutes { get; set; } = DefaultWeatherRefreshMinutes;
    public List<string> OverlayWidgetOrder { get; set; } = [.. OverlayWidgetOrderSettings.Defaults.Select(widget => widget.ToString())];
    public int LowBatteryThreshold { get; set; } = DefaultLowBatteryThreshold;
    public Dictionary<string, bool> DeviceOverlayVisibility { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DeviceAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoCheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Normalize()
    {
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, MinimumLowBatteryThreshold, MaximumLowBatteryThreshold);
        WeatherRefreshMinutes = Math.Clamp(WeatherRefreshMinutes, MinimumWeatherRefreshMinutes, MaximumWeatherRefreshMinutes);
        TemperatureRefreshSeconds = Math.Clamp(TemperatureRefreshSeconds, MinimumTemperatureRefreshSeconds, MaximumTemperatureRefreshSeconds);
        WeatherLocation = string.IsNullOrWhiteSpace(WeatherLocation) ? DefaultWeatherLocation : WeatherLocation.Trim();
        OverlayWidgetOrder = OverlayWidgetOrderSettings.Normalize(OverlayWidgetOrder);
        DeviceOverlayVisibility = new Dictionary<string, bool>(DeviceOverlayVisibility ?? [], StringComparer.OrdinalIgnoreCase);
        DeviceAliases = new Dictionary<string, string>(
            (DeviceAliases ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.Trim() ?? string.Empty)),
            StringComparer.OrdinalIgnoreCase);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Position changes still apply for this session if settings cannot be persisted.
        }
    }
}
