using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SatelliteEyesWin.Services;

public class MapImageComposer
{
    private const int TileSize = 256;
    private static readonly HttpClient _httpClient;

    static MapImageComposer()
    {
        _httpClient = new HttpClient(new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(60) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SatelliteEyesWin/1.0");
    }

    public static async Task<string?> ComposeMapImage(
        double latitude, double longitude, int zoom,
        string tileUrlTemplate, string effectId,
        int screenWidth, int screenHeight,
        string cacheDir, bool skipCache = false,
        string? locationName = null,
        bool showWeather = false,
        string weatherPosition = "top-right",
        bool dayNightDimming = false,
        CancellationToken ct = default)
    {
        string hashInput = $"{tileUrlTemplate}|{latitude:F6}|{longitude:F6}|{zoom}|{screenWidth}|{screenHeight}|{effectId}" +
                           $"|loc:{locationName ?? ""}|weather:{showWeather}|wpos:{weatherPosition}|dimming:{dayNightDimming}";
        string hash = ComputeMd5(hashInput);
        string filePath = Path.Combine(cacheDir, $"map-{hash}.png");

        if (!skipCache && File.Exists(filePath))
            return filePath;

        var grid = MapTile.CalculateTileGrid(latitude, longitude, zoom, screenWidth, screenHeight);

        var tileImages = new Image?[grid.Tiles.Count];
        var tasks = new Task[grid.Tiles.Count];
        var semaphore = new SemaphoreSlim(4);

        for (int i = 0; i < grid.Tiles.Count; i++)
        {
            int idx = i;
            var (tx, ty) = grid.Tiles[i];
            tasks[i] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try { tileImages[idx] = await FetchTile(tileUrlTemplate, tx, ty, zoom, ct); }
                finally { semaphore.Release(); }
            }, ct);
        }
        await Task.WhenAll(tasks);

        using var bitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.FromArgb(30, 30, 30));

            for (int i = 0; i < grid.Tiles.Count; i++)
            {
                if (tileImages[i] == null) continue;
                int col = i % grid.Columns;
                int row = i / grid.Columns;
                int drawX = (int)Math.Round(grid.OriginOffsetX + col * TileSize);
                int drawY = (int)Math.Round(grid.OriginOffsetY + row * TileSize);
                g.DrawImage(tileImages[i]!, drawX, drawY, TileSize, TileSize);
            }
        }

        using var finalBitmap = ImageEffects.Apply(bitmap, effectId);

        if (showWeather)
            await ApplyWeatherOverlay(finalBitmap, latitude, longitude, weatherPosition, ct);

        if (dayNightDimming)
            ApplyDayNightDimming(finalBitmap, latitude, longitude);

        if (!string.IsNullOrWhiteSpace(locationName))
            DrawLocationWatermark(finalBitmap, locationName, latitude, longitude);

        Directory.CreateDirectory(cacheDir);
        finalBitmap.Save(filePath, ImageFormat.Png);

        foreach (var img in tileImages)
            img?.Dispose();

        return filePath;
    }

    private static void DrawLocationWatermark(Bitmap image, string locationName, double lat, double lon)
    {
        const int padding = 20;
        string text = $"{locationName}\n{lat:F4}, {lon:F4}";

        using var g = Graphics.FromImage(image);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
        using var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.FromArgb(180, 255, 255, 255));

        var size = g.MeasureString(text, font);
        float x = padding;
        float y = image.Height - size.Height - padding;

        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0)
                    g.DrawString(text, font, shadowBrush, x + dx, y + dy);

        g.DrawString(text, font, textBrush, x, y);
    }

    private static double GetSolarElevation(double lat, double lon)
    {
        var now = DateTime.UtcNow;
        double declination = 23.45 * Math.Sin(Math.PI / 180.0 * (360.0 / 365.0 * (now.DayOfYear - 81)));
        double decRad = declination * Math.PI / 180.0;
        double latRad = lat * Math.PI / 180.0;
        double haRad = ((now.Hour + now.Minute / 60.0) - (12.0 - lon / 15.0)) * 15.0 * Math.PI / 180.0;

        double sinEl = Math.Sin(latRad) * Math.Sin(decRad) +
                       Math.Cos(latRad) * Math.Cos(decRad) * Math.Cos(haRad);
        return Math.Asin(sinEl) * 180.0 / Math.PI;
    }

    private static void ApplyDayNightDimming(Bitmap image, double lat, double lon)
    {
        double el = GetSolarElevation(lat, lon);
        int alpha = el < -6 ? 80 : el < 0 ? 40 : 0;
        if (alpha == 0) return;

        using var g = Graphics.FromImage(image);
        using var brush = new SolidBrush(Color.FromArgb(alpha, 10, 20, 60));
        g.FillRectangle(brush, 0, 0, image.Width, image.Height);
    }

    private static async Task ApplyWeatherOverlay(
        Bitmap image, double lat, double lon, string position, CancellationToken ct)
    {
        const int RadarZoom = 7, GridSize = 3;
        const int OverlayW = 300, RadarSize = 200, Margin = 24, Pad = 14;

        try
        {
            var weatherTask = FetchWeatherData(lat, lon, ct);
            var radarTask = FetchRadarBitmap(lat, lon, RadarZoom, GridSize, ct);
            await Task.WhenAll(weatherTask, radarTask);

            var weather = weatherTask.Result;
            using var radarBitmap = radarTask.Result;

            if (weather == null && radarBitmap == null) return;

            int cursorY = Pad;
            int currentBlockH = weather != null ? 70 : 0;
            int radarBlockH = radarBitmap != null ? RadarSize + 8 : 0;
            int forecastBlockH = weather?.ForecastDays?.Count > 0 ? 56 : 0;
            int overlayH = Pad + currentBlockH + radarBlockH + forecastBlockH + Pad;

            bool isRight = position.Contains("right");
            bool isBottom = position.Contains("bottom");
            int ox = isRight ? image.Width - OverlayW - Margin : Margin;
            int oy = isBottom ? image.Height - overlayH - Margin : Margin;

            using var g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using var clipPath = CreateRoundedRect(ox, oy, OverlayW, overlayH, 14);
            g.SetClip(clipPath);

            using var bgBrush = new SolidBrush(Color.FromArgb(210, 15, 20, 35));
            g.FillRectangle(bgBrush, ox, oy, OverlayW, overlayH);

            using var white = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
            using var dim = new SolidBrush(Color.FromArgb(150, 200, 210, 230));
            using var accent = new SolidBrush(Color.FromArgb(200, 100, 180, 255));
            using var separator = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);

            if (weather != null)
            {
                using var tempFont = new Font("Segoe UI", 28f, FontStyle.Regular, GraphicsUnit.Point);
                using var descFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
                using var detailFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
                using var sunFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

                string tempStr = $"{weather.Temperature:F0}\u00b0C";
                g.DrawString(tempStr, tempFont, white, ox + Pad, oy + cursorY - 4);

                var tempSize = g.MeasureString(tempStr, tempFont);
                float descX = ox + Pad + tempSize.Width + 4;
                g.DrawString(weather.Description, descFont, accent, descX, oy + cursorY + 4);
                g.DrawString($"Wind {weather.WindSpeed:F0} km/h  \u00b7  Humidity {weather.Humidity}%",
                    detailFont, dim, descX, oy + cursorY + 24);

                if (weather.Sunrise != null || weather.Sunset != null)
                {
                    using var sunBrush = new SolidBrush(Color.FromArgb(180, 255, 200, 80));
                    float rightEdge = ox + OverlayW - Pad;
                    if (weather.Sunrise != null)
                    {
                        string s = $"\u2600\u2191 {weather.Sunrise}";
                        g.DrawString(s, sunFont, sunBrush, rightEdge - g.MeasureString(s, sunFont).Width, oy + cursorY - 2);
                    }
                    if (weather.Sunset != null)
                    {
                        string s = $"\u2600\u2193 {weather.Sunset}";
                        g.DrawString(s, sunFont, sunBrush, rightEdge - g.MeasureString(s, sunFont).Width, oy + cursorY + 14);
                    }
                }

                cursorY += currentBlockH;
                g.DrawLine(separator, ox + Pad, oy + cursorY - 6, ox + OverlayW - Pad, oy + cursorY - 6);
            }

            if (radarBitmap != null)
            {
                int radarX = ox + (OverlayW - RadarSize) / 2;
                int radarY = oy + cursorY;

                var colorMatrix = new ColorMatrix { Matrix33 = 0.9f };
                using var attrs = new ImageAttributes();
                attrs.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(radarBitmap, new Rectangle(radarX, radarY, RadarSize, RadarSize),
                    0, 0, GridSize * TileSize, GridSize * TileSize, GraphicsUnit.Pixel, attrs);

                float cx = radarX + RadarSize / 2f, cy = radarY + RadarSize / 2f;
                using var dotBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
                g.FillEllipse(dotBrush, cx - 3, cy - 3, 6, 6);
                using var dotPen = new Pen(Color.FromArgb(180, 0, 0, 0), 1f);
                g.DrawEllipse(dotPen, cx - 3, cy - 3, 6, 6);

                using var labelFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
                g.DrawString("Radar", labelFont, dim, radarX + RadarSize - 38, radarY + RadarSize - 16);

                cursorY += RadarSize + 8;
                if (weather?.ForecastDays?.Count > 0)
                    g.DrawLine(separator, ox + Pad, oy + cursorY - 4, ox + OverlayW - Pad, oy + cursorY - 4);
            }

            if (weather?.ForecastDays?.Count > 0)
            {
                using var dayFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
                using var tempFont2 = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
                using var descFont2 = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

                int colW = (OverlayW - 2 * Pad) / weather.ForecastDays.Count;
                for (int i = 0; i < weather.ForecastDays.Count; i++)
                {
                    var day = weather.ForecastDays[i];
                    float fx = ox + Pad + i * colW, fy = oy + cursorY;
                    g.DrawString(day.DayName, dayFont, dim, fx + 2, fy);
                    g.DrawString($"{day.High:F0}° / {day.Low:F0}°", tempFont2, white, fx + 2, fy + 16);
                    g.DrawString(day.Description, descFont2, accent, fx + 2, fy + 34);
                }
            }

            g.ResetClip();
            using var borderPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1.5f);
            g.DrawPath(borderPen, clipPath);
        }
        catch { }
    }

    private static async Task<Bitmap?> FetchRadarBitmap(
        double lat, double lon, int radarZoom, int gridSize, CancellationToken ct)
    {
        try
        {
            string? timestamp = await GetRainViewerTimestamp(ct);
            if (timestamp == null) return null;

            int centerX = (int)Math.Floor(MapTile.LongitudeToX(lon, radarZoom));
            int centerY = (int)Math.Floor(MapTile.LatitudeToY(lat, radarZoom));
            int startX = centerX - gridSize / 2, startY = centerY - gridSize / 2;

            var tiles = new Image?[gridSize * gridSize];
            var tasks = new Task[gridSize * gridSize];
            var sem = new SemaphoreSlim(4);

            for (int row = 0; row < gridSize; row++)
            for (int col = 0; col < gridSize; col++)
            {
                int idx = row * gridSize + col;
                int tx = startX + col, ty = startY + row;
                tasks[idx] = Task.Run(async () =>
                {
                    await sem.WaitAsync(ct);
                    try
                    {
                        string url = $"https://tilecache.rainviewer.com/v2/radar/{timestamp}/256/{radarZoom}/{tx}/{ty}/2/1_1.png";
                        tiles[idx] = await FetchTileFromUrl(url, ct);
                    }
                    finally { sem.Release(); }
                }, ct);
            }
            await Task.WhenAll(tasks);

            int rawSize = gridSize * TileSize;
            var bitmap = new Bitmap(rawSize, rawSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(40, 20, 30, 50));
                for (int row = 0; row < gridSize; row++)
                for (int col = 0; col < gridSize; col++)
                {
                    int idx = row * gridSize + col;
                    if (tiles[idx] != null)
                        g.DrawImage(tiles[idx]!, col * TileSize, row * TileSize, TileSize, TileSize);
                }
            }

            foreach (var img in tiles) img?.Dispose();
            return bitmap;
        }
        catch { return null; }
    }

    private record WeatherData(
        double Temperature, int Humidity, double WindSpeed, string Description,
        string? Sunrise, string? Sunset, List<ForecastDay> ForecastDays);

    private record ForecastDay(string DayName, double High, double Low, string Description);

    private static async Task<WeatherData?> FetchWeatherData(double lat, double lon, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.open-meteo.com/v1/forecast?" +
                $"latitude={lat:F4}&longitude={lon:F4}" +
                $"&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m" +
                $"&daily=temperature_2m_max,temperature_2m_min,weather_code,sunrise,sunset" +
                $"&timezone=auto&forecast_days=3";

            var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            var current = root.GetProperty("current");
            double temp = current.GetProperty("temperature_2m").GetDouble();
            int humidity = current.GetProperty("relative_humidity_2m").GetInt32();
            double wind = current.GetProperty("wind_speed_10m").GetDouble();
            int code = current.GetProperty("weather_code").GetInt32();

            var daily = root.GetProperty("daily");
            var maxTemps = daily.GetProperty("temperature_2m_max");
            var minTemps = daily.GetProperty("temperature_2m_min");
            var codes = daily.GetProperty("weather_code");
            var times = daily.GetProperty("time");

            string? sunrise = null, sunset = null;
            if (daily.TryGetProperty("sunrise", out var srArr) && srArr.GetArrayLength() > 0)
                if (DateTime.TryParse(srArr[0].GetString() ?? "", out var sr))
                    sunrise = sr.ToString("h:mm tt");
            if (daily.TryGetProperty("sunset", out var ssArr) && ssArr.GetArrayLength() > 0)
                if (DateTime.TryParse(ssArr[0].GetString() ?? "", out var ss))
                    sunset = ss.ToString("h:mm tt");

            var forecast = new List<ForecastDay>();
            for (int i = 0; i < times.GetArrayLength() && i < 3; i++)
            {
                string dateStr = times[i].GetString() ?? "";
                string dayName = i == 0 ? "Today" :
                    DateTime.TryParse(dateStr, out var dt) ? dt.ToString("ddd") : $"Day {i + 1}";
                forecast.Add(new ForecastDay(dayName, maxTemps[i].GetDouble(), minTemps[i].GetDouble(),
                    WmoCodeToShort(codes[i].GetInt32())));
            }

            return new WeatherData(temp, humidity, wind, WmoCodeToDescription(code), sunrise, sunset, forecast);
        }
        catch { return null; }
    }

    private static string WmoCodeToDescription(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm + hail",
        _ => "Unknown"
    };

    private static string WmoCodeToShort(int code) => code switch
    {
        0 => "Clear",
        1 or 2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 or 56 or 57 => "Drizzle",
        61 or 63 or 65 or 66 or 67 => "Rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow shwrs",
        95 or 96 or 99 => "Storms",
        _ => "—"
    };

    private static GraphicsPath CreateRoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        int d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static async Task<string?> GetRainViewerTimestamp(CancellationToken ct)
    {
        try
        {
            var resp = await _httpClient.GetAsync("https://api.rainviewer.com/public/weather-maps.json", ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            if (doc.RootElement.TryGetProperty("radar", out var radar) &&
                radar.TryGetProperty("past", out var past) &&
                past.GetArrayLength() > 0)
            {
                var last = past[past.GetArrayLength() - 1];
                if (last.TryGetProperty("path", out var path))
                {
                    string p = path.GetString() ?? "";
                    int i = p.LastIndexOf('/');
                    if (i >= 0 && i < p.Length - 1)
                        return p[(i + 1)..];
                }
            }
        }
        catch { }
        return null;
    }

    private static async Task<Image?> FetchTileFromUrl(string url, CancellationToken ct)
    {
        try
        {
            var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var ms = new MemoryStream(bytes); // stream must outlive the Image
            return Image.FromStream(ms);
        }
        catch { return null; }
    }

    private static async Task<Image?> FetchTile(string template, int x, int y, int zoom, CancellationToken ct)
    {
        try
        {
            string url = MapTile.BuildTileUrl(template, x, y, zoom);
            var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            var ms = new MemoryStream(bytes); // stream must outlive the Image
            return Image.FromStream(ms);
        }
        catch { return null; }
    }

    private static string ComputeMd5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static void CleanCache(string cacheDir, int keepCount = 20)
    {
        if (!Directory.Exists(cacheDir)) return;

        var old = new DirectoryInfo(cacheDir)
            .GetFiles("map-*.png")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(keepCount);

        foreach (var f in old)
            try { f.Delete(); } catch { }
    }
}
