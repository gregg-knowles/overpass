using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Overpass.Models;
using Microsoft.Win32;

namespace Overpass.Services;

public enum MapStatus { Offline, Updating, Success, Error }

public class MapManager
{
    public event Action<MapStatus, string>? StatusChanged;

    private readonly LocationService _locationService;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private DateTime _lastUpdateTime;
    private bool _isUpdating;

    public MapManager(LocationService locationService, AppSettings settings)
    {
        _locationService = locationService;
        _settings = settings;
        _locationService.LocationChanged += (_, _) => _ = UpdateMapAsync();
        SystemEvents.DisplaySettingsChanged += (_, _) => _ = UpdateMapAsync();
    }

    public void Start()
    {
        StatusChanged?.Invoke(MapStatus.Offline, "Starting...");

        if (_settings.UseCurrentLocation)
            _locationService.StartCurrentLocation();
        else
            _locationService.StartRandomLocations(_settings.RandomLocationCategory, _settings.RotationIntervalSeconds);
    }

    public async Task UpdateMapAsync(bool force = false)
    {
        if (_isUpdating) return;
        if (!_locationService.HasLocation)
        {
            StatusChanged?.Invoke(MapStatus.Offline, "Waiting for location...");
            return;
        }

        _isUpdating = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            StatusChanged?.Invoke(MapStatus.Updating, "Updating the map...");

            var style = MapStyles.GetById(_settings.MapStyleId);
            int zoom = Math.Clamp(_settings.ZoomLevel, style.MinZoom, style.MaxZoom);
            var bounds = GetVirtualDesktopBounds();

            string? locationName = _settings.ShowLocationWatermark ? _locationService.CurrentLocationName : null;

            string? imagePath = await ComposeMultiMonitorMap(
                _locationService.Latitude, _locationService.Longitude, zoom,
                style.Source, _settings.ImageEffectId, bounds, AppSettings.CacheDir,
                skipCache: force, locationName: locationName,
                showWeather: _settings.ShowWeatherOverlay,
                weatherPosition: _settings.WeatherOverlayPosition,
                dayNightDimming: _settings.DayNightDimming,
                ct: _cts.Token);

            if (imagePath != null)
            {
                bool ok = WallpaperManager.SetWallpaper(imagePath);
                if (_settings.SetLockScreen)
                    _ = WallpaperManager.SetLockScreenImage(imagePath);

                if (ok)
                {
                    _lastUpdateTime = DateTime.Now;
                    StatusChanged?.Invoke(MapStatus.Success,
                        _locationService.CurrentLocationName ?? $"Map updated ({_lastUpdateTime:HH:mm})");
                }
                else
                    StatusChanged?.Invoke(MapStatus.Error, "Failed to set wallpaper");
            }
            else
                StatusChanged?.Invoke(MapStatus.Error, "Failed to download map tiles");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(MapStatus.Error, $"Error: {ex.Message}");
        }
        finally { _isUpdating = false; }
    }

    public void ApplySettings()
    {
        _locationService.Stop();
        Start();
    }

    private static double GetDpiScale()
    {
        try
        {
            var prop = typeof(SystemParameters).GetProperty("DpiX",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (prop != null)
                return (int)prop.GetValue(null, null)! / 96.0;
        }
        catch { }
        return 1.0;
    }

    private static Rectangle GetVirtualDesktopBounds()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            var scale = GetDpiScale();
            return new Rectangle(0, 0,
                (int)(SystemParameters.PrimaryScreenWidth * scale),
                (int)(SystemParameters.PrimaryScreenHeight * scale));
        }

        int left = int.MaxValue, top = int.MaxValue;
        int right = int.MinValue, bottom = int.MinValue;
        foreach (var s in screens)
        {
            if (s.Bounds.Left < left) left = s.Bounds.Left;
            if (s.Bounds.Top < top) top = s.Bounds.Top;
            if (s.Bounds.Right > right) right = s.Bounds.Right;
            if (s.Bounds.Bottom > bottom) bottom = s.Bounds.Bottom;
        }
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static async Task<string?> ComposeMultiMonitorMap(
        double lat, double lon, int zoom,
        string tileUrlTemplate, string effectId,
        Rectangle virtualBounds, string cacheDir,
        bool skipCache = false, string? locationName = null,
        bool showWeather = false, string weatherPosition = "top-right",
        bool dayNightDimming = false, CancellationToken ct = default)
    {
        var screens = Screen.AllScreens;

        if (screens.Length <= 1)
        {
            return await MapImageComposer.ComposeMapImage(
                lat, lon, zoom, tileUrlTemplate, effectId,
                virtualBounds.Width, virtualBounds.Height, cacheDir,
                skipCache: skipCache, locationName: locationName,
                showWeather: showWeather, weatherPosition: weatherPosition,
                dayNightDimming: dayNightDimming, ct: ct);
        }

        // multi-monitor: compose each screen then stitch
        string geometry = string.Join(";",
            screens.Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}"));
        string hashInput = $"multi|{tileUrlTemplate}|{lat:F6}|{lon:F6}|{zoom}|{effectId}|{geometry}" +
                           $"|loc:{locationName ?? ""}|weather:{showWeather}|wpos:{weatherPosition}|dimming:{dayNightDimming}";
        string hash = ComputeMd5(hashInput);
        string filePath = System.IO.Path.Combine(cacheDir, $"map-{hash}.png");

        if (!skipCache && System.IO.File.Exists(filePath))
            return filePath;

        var monitorImages = await Task.WhenAll(
            screens.Select(s => MapImageComposer.ComposeMapImage(
                lat, lon, zoom, tileUrlTemplate, effectId,
                s.Bounds.Width, s.Bounds.Height, cacheDir,
                skipCache: skipCache, locationName: locationName,
                showWeather: showWeather, weatherPosition: weatherPosition,
                dayNightDimming: dayNightDimming, ct: ct)));

        using var canvas = new Bitmap(virtualBounds.Width, virtualBounds.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Black);
            for (int i = 0; i < screens.Length; i++)
            {
                if (monitorImages[i] == null) continue;
                using var img = Image.FromFile(monitorImages[i]!);
                g.DrawImage(img,
                    screens[i].Bounds.X - virtualBounds.X,
                    screens[i].Bounds.Y - virtualBounds.Y,
                    screens[i].Bounds.Width, screens[i].Bounds.Height);
            }
        }

        System.IO.Directory.CreateDirectory(cacheDir);
        canvas.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        return filePath;
    }

    private static string ComputeMd5(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string? GetBrowserUrl()
    {
        if (!_locationService.HasLocation) return null;
        var style = MapStyles.GetById(_settings.MapStyleId);
        return style.BrowserUrl?
            .Replace("{latitude}", _locationService.Latitude.ToString("F6"))
            .Replace("{longitude}", _locationService.Longitude.ToString("F6"))
            .Replace("{zoom}", _settings.ZoomLevel.ToString());
    }

    public string? GetWindyUrl()
    {
        if (!_locationService.HasLocation) return null;
        return $"https://www.windy.com/{_locationService.Latitude:F4}/{_locationService.Longitude:F4}" +
               $"?radar,{_locationService.Latitude:F4},{_locationService.Longitude:F4},10";
    }
}
