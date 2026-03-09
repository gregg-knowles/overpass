using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Overpass.Models;

namespace Overpass.Services;

public class LocationService
{
    public event Action<double, double>? LocationChanged;
    public event Action<string>? LocationNameChanged;
    public event Action<string>? LocationError;

    private readonly List<NamedLocation> _locations = new();
    private readonly Random _rng = new();
    private System.Threading.Timer? _rotationTimer;
    private static readonly HttpClient _http = new();

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public bool HasLocation { get; private set; }
    public string? CurrentLocationName { get; private set; }

    public LocationService()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("locations.json"));
            if (resName == null) return;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            var locs = JsonSerializer.Deserialize<List<NamedLocation>>(reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (locs != null) _locations.AddRange(locs);
        }
        catch { }
    }

    public void StartCurrentLocation(int pollingIntervalMinutes = 10)
    {
        StopTimer();
        _ = GetLocationAsync();

        if (pollingIntervalMinutes > 0)
        {
            var interval = TimeSpan.FromMinutes(pollingIntervalMinutes);
            _rotationTimer = new System.Threading.Timer(
                _ => _ = GetLocationAsync(), null, interval, interval);
        }
    }

    private async Task GetLocationAsync()
    {
        // try Windows geolocation first
        try
        {
            var geo = new Windows.Devices.Geolocation.Geolocator
            {
                DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var pos = await geo.GetGeopositionAsync().AsTask(cts.Token);
            var c = pos.Coordinate.Point.Position;
            if (c.Latitude != 0 || c.Longitude != 0)
            {
                SetPosition(c.Latitude, c.Longitude);
                CurrentLocationName = null;
                return;
            }
        }
        catch { }

        // fall back to IP geolocation — try multiple providers in order
        LocationError?.Invoke("Using IP-based location...");
        var result = await TryIpProviders();
        if (result != null)
        {
            SetPosition(result.Value.lat, result.Value.lon);
            CurrentLocationName = !string.IsNullOrEmpty(result.Value.city) ? result.Value.city : null;
            if (CurrentLocationName != null) LocationNameChanged?.Invoke(CurrentLocationName);
        }
        else
        {
            LocationError?.Invoke("Could not determine location. Check your internet connection.");
        }
    }

    public void StartRandomLocations(string category, int intervalSeconds)
    {
        PickRandomLocation(category);
        StopTimer();
        _rotationTimer = new System.Threading.Timer(
            _ => PickRandomLocation(category), null,
            TimeSpan.FromSeconds(intervalSeconds),
            TimeSpan.FromSeconds(intervalSeconds));
    }

    public void PickRandomLocation(string category = "")
    {
        var pool = string.IsNullOrEmpty(category)
            ? _locations
            : _locations.Where(l => l.Category == category).ToList();

        if (pool.Count == 0)
        {
            SetPosition(40.7128, -74.0060);
            CurrentLocationName = "New York City";
            LocationNameChanged?.Invoke(CurrentLocationName);
            return;
        }

        var loc = pool[_rng.Next(pool.Count)];
        SetPosition(loc.Latitude, loc.Longitude);
        CurrentLocationName = loc.Name;
        LocationNameChanged?.Invoke(loc.Name);
    }

    private static async Task<(double lat, double lon, string city)?> TryIpProviders()
    {
        // 1. ipapi.co (HTTPS, 1000/day free)
        try
        {
            var json = await _http.GetStringAsync("https://ipapi.co/json/");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (
                root.GetProperty("latitude").GetDouble(),
                root.GetProperty("longitude").GetDouble(),
                root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "");
        }
        catch { }

        // 2. ip-api.com (HTTP only, 45/min free)
        try
        {
            var json = await _http.GetStringAsync("http://ip-api.com/json/?fields=lat,lon,city");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (
                root.GetProperty("lat").GetDouble(),
                root.GetProperty("lon").GetDouble(),
                root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "");
        }
        catch { }

        // 3. ipwho.is (HTTPS, 10000/month free)
        try
        {
            var json = await _http.GetStringAsync("https://ipwho.is/");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (
                root.GetProperty("latitude").GetDouble(),
                root.GetProperty("longitude").GetDouble(),
                root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "");
        }
        catch { }

        // 4. freeipapi.com (HTTPS, 60/min free)
        try
        {
            var json = await _http.GetStringAsync("https://freeipapi.com/api/json");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (
                root.GetProperty("latitude").GetDouble(),
                root.GetProperty("longitude").GetDouble(),
                root.TryGetProperty("cityName", out var c) ? c.GetString() ?? "" : "");
        }
        catch { }

        return null;
    }

    private void SetPosition(double lat, double lon)
    {
        bool moved = !HasLocation || DistanceKm(Latitude, Longitude, lat, lon) > 0.5;
        Latitude = lat;
        Longitude = lon;
        HasLocation = true;
        if (moved) LocationChanged?.Invoke(lat, lon);
    }

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private void StopTimer()
    {
        _rotationTimer?.Dispose();
        _rotationTimer = null;
    }

    public void Stop() => StopTimer();
}
