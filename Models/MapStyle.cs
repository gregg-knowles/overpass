namespace Overpass.Models;

public class MapStyle
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Source2x { get; set; }
    public int MaxZoom { get; set; } = 20;
    public int MinZoom { get; set; } = 10;
    public bool UpscaleRetina { get; set; }
    public string? BrowserUrl { get; set; }
    public string? LogoImage { get; set; }
}
