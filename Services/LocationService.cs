using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using SatelliteEyesWin.Models;

namespace SatelliteEyesWin.Services;

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

    public void StartCurrentLocation()
    {
        StopTimer();
        _ = GetLocationAsync();
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

        // fall back to IP geolocation
        try
        {
            LocationError?.Invoke("Using IP-based location...");
            var json = await _http.GetStringAsync("https://ipapi.co/json/");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double lat = root.GetProperty("latitude").GetDouble();
            double lon = root.GetProperty("longitude").GetDouble();
            string city = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";

            SetPosition(lat, lon);
            CurrentLocationName = !string.IsNullOrEmpty(city) ? city : null;
            if (CurrentLocationName != null) LocationNameChanged?.Invoke(CurrentLocationName);
        }
        catch (Exception ex)
        {
            LocationError?.Invoke($"Could not determine location: {ex.Message}");
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

    private void SetPosition(double lat, double lon)
    {
        Latitude = lat;
        Longitude = lon;
        HasLocation = true;
        LocationChanged?.Invoke(lat, lon);
    }

    private void StopTimer()
    {
        _rotationTimer?.Dispose();
        _rotationTimer = null;
    }

    public void Stop() => StopTimer();
}
