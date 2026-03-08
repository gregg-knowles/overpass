using SatelliteEyesWin.Models;

namespace SatelliteEyesWin.Services;

public static class MapStyles
{
    public static readonly MapStyle[] BuiltIn = new[]
    {
        new MapStyle
        {
            Id = "google-satellite",
            Name = "Google Satellite",
            Source = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}",
            MaxZoom = 20,
            MinZoom = 10,
            UpscaleRetina = true,
            BrowserUrl = "https://www.google.com/maps/@{latitude},{longitude},{zoom}z"
        },
        new MapStyle
        {
            Id = "google-hybrid",
            Name = "Google Hybrid",
            Source = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}",
            MaxZoom = 20,
            MinZoom = 10,
            UpscaleRetina = true,
            BrowserUrl = "https://www.google.com/maps/@{latitude},{longitude},{zoom}z"
        },
        new MapStyle
        {
            Id = "google-terrain",
            Name = "Google Terrain",
            Source = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}",
            MaxZoom = 20,
            MinZoom = 10,
            UpscaleRetina = true,
            BrowserUrl = "https://www.google.com/maps/@{latitude},{longitude},{zoom}z"
        },
        new MapStyle
        {
            Id = "bing-aerial",
            Name = "Bing Aerial",
            Source = "https://ecn.t3.tiles.virtualearth.net/tiles/a{q}.jpeg?g=915",
            MaxZoom = 17,
            MinZoom = 10,
            BrowserUrl = "https://www.bing.com/maps?cp={latitude}~{longitude}&lvl={zoom}&style=a"
        },
        new MapStyle
        {
            Id = "osm-standard",
            Name = "OpenStreetMap",
            Source = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            MaxZoom = 19,
            MinZoom = 10,
            BrowserUrl = "https://www.openstreetmap.org/#map={zoom}/{latitude}/{longitude}"
        },
        new MapStyle
        {
            Id = "stamen-watercolor",
            Name = "Stamen Watercolor",
            Source = "https://watercolormaps.collection.cooperhewitt.org/tile/watercolor/{z}/{x}/{y}.jpg",
            MaxZoom = 17,
            MinZoom = 10,
        },
        new MapStyle
        {
            Id = "esri-world-imagery",
            Name = "Esri World Imagery",
            Source = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            MaxZoom = 19,
            MinZoom = 10,
        },
        new MapStyle
        {
            Id = "esri-world-topo",
            Name = "Esri World Topo",
            Source = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}",
            MaxZoom = 19,
            MinZoom = 10,
        },
        new MapStyle
        {
            Id = "esri-natgeo",
            Name = "Esri NatGeo",
            Source = "https://server.arcgisonline.com/ArcGIS/rest/services/NatGeo_World_Map/MapServer/tile/{z}/{y}/{x}",
            MaxZoom = 16,
            MinZoom = 10,
        },
        new MapStyle
        {
            Id = "carto-dark",
            Name = "CartoDB Dark",
            Source = "https://basemaps.cartocdn.com/dark_all/{z}/{x}/{y}@2x.png",
            MaxZoom = 20,
            MinZoom = 10,
        },
        new MapStyle
        {
            Id = "carto-voyager",
            Name = "CartoDB Voyager",
            Source = "https://basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png",
            MaxZoom = 20,
            MinZoom = 10,
        },
    };

    public static MapStyle GetById(string id)
    {
        return BuiltIn.FirstOrDefault(s => s.Id == id) ?? BuiltIn[0];
    }
}
