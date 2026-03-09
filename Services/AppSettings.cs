using System.IO;
using System.Text.Json;
using Overpass.Models;

namespace Overpass.Services;

public class AppSettings
{
    public string MapStyleId { get; set; } = "google-satellite";
    public int ZoomLevel { get; set; } = 15;
    public string ImageEffectId { get; set; } = "none";
    public bool UseCurrentLocation { get; set; } = false;
    public string RandomLocationCategory { get; set; } = "";
    public int RotationIntervalSeconds { get; set; } = 86400;
    public bool LaunchAtStartup { get; set; } = false;
    public bool HasCompletedFirstRun { get; set; } = false;
    public bool SetLockScreen { get; set; } = true;
    public bool ShowLocationWatermark { get; set; } = true;
    public bool ShowWeatherOverlay { get; set; } = false;
    public string WeatherOverlayPosition { get; set; } = "top-right";
    public bool DayNightDimming { get; set; } = true;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Overpass");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static string CacheDir => Path.Combine(SettingsDir, "cache");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                s.Sanitize();
                return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    private void Sanitize()
    {
        if (!MapStyles.BuiltIn.Any(s => s.Id == MapStyleId))
            MapStyleId = "google-satellite";
        if (!ImageEffect.BuiltIn.Any(e => e.Id == ImageEffectId))
            ImageEffectId = "none";
        ZoomLevel = Math.Clamp(ZoomLevel, 1, 22);
        RotationIntervalSeconds = Math.Clamp(RotationIntervalSeconds, 60, 604800);
        string[] validPositions = ["top-right", "top-left", "bottom-right", "bottom-left"];
        if (!validPositions.Contains(WeatherOverlayPosition))
            WeatherOverlayPosition = "top-right";
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
