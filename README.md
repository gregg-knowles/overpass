# Overpass

Sets your desktop wallpaper to satellite imagery of your location. A Windows port of [Satellite Eyes for macOS](https://github.com/tomtaylor/satellite-eyes) by Tom Taylor.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/github/license/gregg-knowles/overpass)
![GitHub release](https://img.shields.io/github/v/release/gregg-knowles/overpass)
![Downloads](https://img.shields.io/github/downloads/gregg-knowles/overpass/total)
![GitHub stars](https://img.shields.io/github/stars/gregg-knowles/overpass)
![GitHub issues](https://img.shields.io/github/issues/gregg-knowles/overpass)

## Features

- **Live location** via Windows GPS or IP-based fallback
- **Curated locations** — airports, World Heritage Sites, and other interesting sights with automatic rotation
- **10 map styles** — Google Satellite, Bing Aerial, OpenStreetMap, Esri World Imagery, CartoDB, Stamen Watercolour, and more
- **Image effects** — darken, desaturate, pixellate, blur
- **Weather radar overlay** with current conditions and 3-day forecast (Open-Meteo + RainViewer)
- **Day/night dimming** based on solar elevation
- **Location watermark** on the wallpaper
- **Lock screen** image support
- **Multi-monitor** — spans or composes across all displays
- **System tray** — runs quietly in the background
- **Launch at startup** option

## Screenshot

<img width="2879" height="1799" alt="image" src="https://github.com/user-attachments/assets/feff5fa5-87d0-4c94-ad8f-21d6271b55ed" />

<img width="479" height="817" alt="image" src="https://github.com/user-attachments/assets/011030ce-c88b-4f7b-8390-185050a5be66" />



## Requirements

- Windows 10 (build 19041) or later
- .NET 8 Runtime (for the small build) or no dependencies (for the standalone build)

## Download

Grab the latest release from the [Releases](https://github.com/gregg-knowles/overpass/releases) page:

- **Standalone** (~180 MB) — includes the .NET runtime, runs anywhere
- **Small** (~25 MB) — requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) installed

## Building from source

```
git clone https://github.com/gregg-knowles/overpass.git
cd overpass
dotnet build
```

To produce release binaries:

```
publish.bat
```

This creates both standalone and framework-dependent single-file executables in the `publish/` folder.

## How it works

1. Determines your location via the Windows Geolocation API, falling back to IP geolocation (ipapi.co)
2. Fetches map tiles from the selected provider and composites them to match your screen resolution
3. Optionally applies image effects, weather overlay, day/night dimming, and a location watermark
4. Sets the result as your desktop wallpaper (and optionally lock screen)

## Configuration

All settings are accessible from the main window. Settings are stored in:

```
%APPDATA%\Overpass\settings.json
```

Cached map images are stored in:

```
%APPDATA%\Overpass\cache\
```

## GPS location troubleshooting

If GPS location isn't working, check your Windows settings:

1. **Settings > Privacy & Security > Location**
2. Ensure **Location services** is turned on
3. Scroll to the bottom and enable **Let desktop apps access your location**

If GPS still fails, the app falls back to IP-based geolocation automatically.

## Credits

- Original [Satellite Eyes for macOS](https://github.com/tomtaylor/satellite-eyes) by [Tom Taylor](https://tomtaylor.co.uk/)
- Weather data from [Open-Meteo](https://open-meteo.com/) (free, no API key required)
- Radar imagery from [RainViewer](https://www.rainviewer.com/)
- Map tiles from Google, Bing, OpenStreetMap, Esri, CartoDB, and Stamen

## Licence

MIT
