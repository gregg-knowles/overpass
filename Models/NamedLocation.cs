namespace Overpass.Models;

public class NamedLocation
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
