using System.Text;

namespace SatelliteEyesWin.Services;

public static class MapTile
{
    private const int TileSize = 256;

    public static double LatitudeToY(double latitude, int zoom)
    {
        double latRad = latitude * Math.PI / 180.0;
        double n = 1 << zoom;
        return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
    }

    public static double LongitudeToX(double longitude, int zoom)
    {
        double n = 1 << zoom;
        return (longitude + 180.0) / 360.0 * n;
    }

    public static (double lat, double lon) TileToCoordinate(int x, int y, int zoom)
    {
        double n = 1 << zoom;
        double lon = x / n * 360.0 - 180.0;
        double latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
        double lat = latRad * 180.0 / Math.PI;
        return (lat, lon);
    }

    public static string ToQuadKey(int x, int y, int zoom)
    {
        var sb = new StringBuilder(zoom);
        for (int i = zoom; i > 0; i--)
        {
            char digit = '0';
            int mask = 1 << (i - 1);
            if ((x & mask) != 0) digit++;
            if ((y & mask) != 0) { digit++; digit++; }
            sb.Append(digit);
        }
        return sb.ToString();
    }

    public static string BuildTileUrl(string template, int x, int y, int zoom)
    {
        return template
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString())
            .Replace("{z}", zoom.ToString())
            .Replace("{q}", ToQuadKey(x, y, zoom));
    }

    public static (int tileX, int tileY, double offsetX, double offsetY) CoordinateToTile(
        double latitude, double longitude, int zoom)
    {
        double fx = LongitudeToX(longitude, zoom);
        double fy = LatitudeToY(latitude, zoom);
        int tileX = (int)Math.Floor(fx);
        int tileY = (int)Math.Floor(fy);
        double offsetX = (fx - tileX) * TileSize;
        double offsetY = (fy - tileY) * TileSize;
        return (tileX, tileY, offsetX, offsetY);
    }

    public static TileGrid CalculateTileGrid(
        double latitude, double longitude, int zoom,
        int screenWidth, int screenHeight)
    {
        double centerFx = LongitudeToX(longitude, zoom);
        double centerFy = LatitudeToY(latitude, zoom);

        double centerPixelX = centerFx * TileSize;
        double centerPixelY = centerFy * TileSize;

        double topPixel = centerPixelY - screenHeight / 2.0;
        double leftPixel = centerPixelX - screenWidth / 2.0;

        int startTileX = (int)Math.Floor(leftPixel / TileSize);
        int startTileY = (int)Math.Floor(topPixel / TileSize);
        int endTileX = (int)Math.Floor((leftPixel + screenWidth - 1) / TileSize);
        int endTileY = (int)Math.Floor((topPixel + screenHeight - 1) / TileSize);

        double originOffsetX = startTileX * TileSize - leftPixel;
        double originOffsetY = startTileY * TileSize - topPixel;

        var tiles = new List<(int x, int y)>();
        int maxTile = (1 << zoom);
        for (int ty = startTileY; ty <= endTileY; ty++)
        {
            for (int tx = startTileX; tx <= endTileX; tx++)
            {
                // Wrap X around the world
                int wrappedX = ((tx % maxTile) + maxTile) % maxTile;
                // Clamp Y
                int clampedY = Math.Clamp(ty, 0, maxTile - 1);
                tiles.Add((wrappedX, clampedY));
            }
        }

        return new TileGrid
        {
            Tiles = tiles,
            Columns = endTileX - startTileX + 1,
            Rows = endTileY - startTileY + 1,
            OriginOffsetX = originOffsetX,
            OriginOffsetY = originOffsetY
        };
    }
}

public class TileGrid
{
    public List<(int x, int y)> Tiles { get; set; } = new();
    public int Columns { get; set; }
    public int Rows { get; set; }
    public double OriginOffsetX { get; set; }
    public double OriginOffsetY { get; set; }
}
