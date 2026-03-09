using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Overpass.Models;
using Overpass.Services;
using Forms = System.Windows.Forms;

namespace Overpass;

public partial class MainWindow : Window
{
    private MapManager? _mapManager;
    private LocationService? _locationService;
    private AppSettings? _settings;
    private Forms.NotifyIcon? _trayIcon;

    private bool _isLoading = true;
    private bool _isExiting;
    private bool _hasShownTrayTip;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _refreshCts;
    private static readonly HttpClient _http;

    static MainWindow()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "Overpass/1.0");
    }

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Overpass",
            Icon = LoadAppIcon(),
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => ShowFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Refresh Map", null, async (_, _) =>
        {
            if (_mapManager != null) await _mapManager.UpdateMapAsync(force: true);
        });
        menu.Items.Add("New Location", null, (_, _) =>
            _locationService?.PickRandomLocation(_settings?.RandomLocationCategory ?? ""));
        menu.Items.Add("Open in Browser", null, (_, _) => OpenUrl(_mapManager?.GetBrowserUrl()));
        menu.Items.Add("View on Windy", null, (_, _) => OpenUrl(_mapManager?.GetWindyUrl()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private static void OpenUrl(string? url)
    {
        if (url == null) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != "https" && uri.Scheme != "http") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = AppSettings.Load();
        _locationService = new LocationService();
        _locationService.LocationError += msg =>
            Dispatcher.BeginInvoke(() => UpdateStatus(MapStatus.Error, msg));

        _mapManager = new MapManager(_locationService, _settings);
        _mapManager.StatusChanged += OnStatusChanged;

        MapImageComposer.CleanCache(AppSettings.CacheDir);

        if (!_settings.HasCompletedFirstRun)
            DoFirstRun();

        LoadSettingsToUI();
        _isLoading = false;
        UpdateButtonStates();

        StatusText.Text = _settings.UseCurrentLocation
            ? "Getting your location..."
            : "Picking a location...";

        try { _mapManager.Start(); }
        catch (Exception ex) { UpdateStatus(MapStatus.Error, $"Startup error: {ex.Message}"); }
    }

    private void LoadSettingsToUI()
    {
        string tag = _settings!.UseCurrentLocation ? "gps" :
            (string.IsNullOrEmpty(_settings.RandomLocationCategory) ? "all" : _settings.RandomLocationCategory);
        SelectComboByTag(LocationSourceCombo, tag);

        foreach (ComboBoxItem item in RotationCombo.Items)
        {
            if (int.Parse((string)item.Tag) == _settings.RotationIntervalSeconds)
            { RotationCombo.SelectedItem = item; break; }
        }
        if (RotationCombo.SelectedItem == null) RotationCombo.SelectedIndex = 2;

        SelectComboByTag(PollingCombo, _settings.GpsPollingIntervalMinutes.ToString());
        if (PollingCombo.SelectedItem == null) PollingCombo.SelectedIndex = 1;

        var pollVis = _settings.UseCurrentLocation ? Visibility.Visible : Visibility.Collapsed;
        PollingLabel.Visibility = pollVis;
        PollingCombo.Visibility = pollVis;

        var rotVis = _settings.UseCurrentLocation ? Visibility.Collapsed : Visibility.Visible;
        RotationLabel.Visibility = rotVis;
        RotationCombo.Visibility = rotVis;

        foreach (var style in MapStyles.BuiltIn)
        {
            var item = new ComboBoxItem { Content = style.Name, Tag = style.Id };
            MapStyleCombo.Items.Add(item);
            if (style.Id == _settings.MapStyleId) MapStyleCombo.SelectedItem = item;
        }
        if (MapStyleCombo.SelectedItem == null) MapStyleCombo.SelectedIndex = 0;

        UpdateZoomRange();
        ZoomSlider.Value = _settings.ZoomLevel;
        UpdateZoomLabel();

        foreach (var effect in ImageEffect.BuiltIn)
        {
            var item = new ComboBoxItem { Content = effect.Name, Tag = effect.Id };
            EffectCombo.Items.Add(item);
            if (effect.Id == _settings.ImageEffectId) EffectCombo.SelectedItem = item;
        }
        if (EffectCombo.SelectedItem == null) EffectCombo.SelectedIndex = 0;

        LockScreenCheck.IsChecked = _settings.SetLockScreen;
        WatermarkCheck.IsChecked = _settings.ShowLocationWatermark;
        DayNightCheck.IsChecked = _settings.DayNightDimming;
        WeatherCheck.IsChecked = _settings.ShowWeatherOverlay;
        SelectComboByTag(WeatherPositionCombo, _settings.WeatherOverlayPosition);
        if (WeatherPositionCombo.SelectedItem == null) WeatherPositionCombo.SelectedIndex = 0;
        WeatherPositionCombo.IsEnabled = _settings.ShowWeatherOverlay;
        StartupCheck.IsChecked = _settings.LaunchAtStartup;
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if ((string)item.Tag == tag) { combo.SelectedItem = item; return; }
    }

    private void DoFirstRun()
    {
        var result = System.Windows.MessageBox.Show(this,
            "Overpass sets your desktop wallpaper to satellite imagery of your location.\n\n" +
            "Would you like to use your current location (GPS)?\n\n" +
            "Click 'Yes' for GPS location, or 'No' to explore interesting sights around the world.\n\n" +
            "(If GPS doesn't work, you can switch in Settings later.)",
            "Welcome to Overpass",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        _settings!.UseCurrentLocation = result == MessageBoxResult.Yes;

        if (_settings.UseCurrentLocation)
        {
            System.Windows.MessageBox.Show(this,
                "GPS mode selected. Make sure Location is enabled in Windows:\n\n" +
                "  Windows Settings > Privacy & Security > Location\n" +
                "  - 'Location services' = ON\n" +
                "  - Scroll to bottom: 'Let desktop apps access your location' = ON\n\n" +
                "If location can't be found, use Settings to switch to Interesting Sights mode.",
                "GPS Location", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        _settings.HasCompletedFirstRun = true;
        _settings.Save();
    }

    private void SaveAndApply(bool restartLocation = false)
    {
        if (_isLoading || _settings == null) return;

        var locTag = (string)((ComboBoxItem)LocationSourceCombo.SelectedItem).Tag;
        _settings.UseCurrentLocation = locTag == "gps";
        _settings.RandomLocationCategory = locTag == "gps" || locTag == "all" ? "" : locTag;
        _settings.RotationIntervalSeconds = int.Parse((string)((ComboBoxItem)RotationCombo.SelectedItem).Tag);
        _settings.GpsPollingIntervalMinutes = int.Parse((string)((ComboBoxItem)PollingCombo.SelectedItem).Tag);
        _settings.MapStyleId = (string)((ComboBoxItem)MapStyleCombo.SelectedItem).Tag;
        _settings.ZoomLevel = (int)ZoomSlider.Value;
        _settings.ImageEffectId = (string)((ComboBoxItem)EffectCombo.SelectedItem).Tag;
        _settings.SetLockScreen = LockScreenCheck.IsChecked == true;
        _settings.ShowLocationWatermark = WatermarkCheck.IsChecked == true;
        _settings.DayNightDimming = DayNightCheck.IsChecked == true;
        _settings.ShowWeatherOverlay = WeatherCheck.IsChecked == true;
        _settings.WeatherOverlayPosition = (string)((ComboBoxItem)WeatherPositionCombo.SelectedItem).Tag;
        _settings.LaunchAtStartup = StartupCheck.IsChecked == true;

        _settings.Save();
        StartupManager.SetLaunchAtStartup(_settings.LaunchAtStartup);

        if (restartLocation) _mapManager?.ApplySettings();
        UpdateButtonStates();
    }

    private void LocationSourceCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || RotationCombo == null) return;
        var tag = (string?)((LocationSourceCombo.SelectedItem as ComboBoxItem)?.Tag);
        bool isGps = tag == "gps";
        PollingLabel.Visibility = isGps ? Visibility.Visible : Visibility.Collapsed;
        PollingCombo.Visibility = isGps ? Visibility.Visible : Visibility.Collapsed;
        RotationLabel.Visibility = isGps ? Visibility.Collapsed : Visibility.Visible;
        RotationCombo.Visibility = isGps ? Visibility.Collapsed : Visibility.Visible;
        SaveAndApply(restartLocation: true);
    }

    private void RotationCombo_Changed(object sender, SelectionChangedEventArgs e)
        => SaveAndApply(restartLocation: true);

    private void PollingCombo_Changed(object sender, SelectionChangedEventArgs e)
        => SaveAndApply(restartLocation: true);

    private void MapStyleCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        UpdateZoomRange();
        SaveAndApply();
        LoadPreview();
        ScheduleWallpaperRefresh();
    }

    private void ZoomSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading || ZoomLabel == null) return;
        UpdateZoomLabel();
        SaveAndApply();
        LoadPreview();
        ScheduleWallpaperRefresh();
    }

    private void EffectCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        SaveAndApply();
        ScheduleWallpaperRefresh();
    }

    private void Checkbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (sender == WeatherCheck)
            WeatherPositionCombo.IsEnabled = WeatherCheck.IsChecked == true;
        SaveAndApply();
        if (sender == WatermarkCheck || sender == DayNightCheck || sender == WeatherCheck)
            ScheduleWallpaperRefresh();
    }

    private void WeatherPositionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        SaveAndApply();
        ScheduleWallpaperRefresh();
    }

    // debounced wallpaper refresh (800ms after last change)
    private void ScheduleWallpaperRefresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, ct);
                if (_mapManager != null) await _mapManager.UpdateMapAsync(force: true);
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private void UpdateZoomRange()
    {
        var styleId = (MapStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings?.MapStyleId ?? "google-satellite";
        var style = MapStyles.GetById(styleId);
        ZoomSlider.Minimum = style.MinZoom;
        ZoomSlider.Maximum = style.MaxZoom;
    }

    private void UpdateZoomLabel()
    {
        int z = (int)ZoomSlider.Value;
        string desc = z switch
        {
            <= 10 => "Region", 11 => "Metro area", 12 => "City", 13 => "Suburbs",
            14 => "Town", 15 => "Neighbourhood", 16 => "Streets", 17 => "Blocks",
            18 => "Buildings", 19 => "Close-up", _ => "Max detail"
        };
        ZoomLabel.Text = $"{z} — {desc}";
    }

    private void LoadPreview()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        double lat = _locationService?.HasLocation == true ? _locationService.Latitude : 48.8584;
        double lon = _locationService?.HasLocation == true ? _locationService.Longitude : 2.2945;

        var styleId = (MapStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings?.MapStyleId ?? "google-satellite";
        var style = MapStyles.GetById(styleId);
        int zoom = (int)ZoomSlider.Value;

        PreviewLoading.Visibility = Visibility.Visible;
        PreviewLoading.Text = "Loading preview...";
        PreviewImage.Source = null;

        _ = LoadPreviewAsync(style.Source, lat, lon, zoom, ct);
    }

    private async Task LoadPreviewAsync(string tileUrl, double lat, double lon, int zoom, CancellationToken ct)
    {
        try
        {
            double fx = MapTile.LongitudeToX(lon, zoom);
            double fy = MapTile.LatitudeToY(lat, zoom);
            string url = MapTile.BuildTileUrl(tileUrl, (int)Math.Floor(fx), (int)Math.Floor(fy), zoom);
            var bytes = await _http.GetByteArrayAsync(url, ct);
            if (ct.IsCancellationRequested) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            Dispatcher.Invoke(() =>
            {
                PreviewImage.Source = bmp;
                PreviewLoading.Visibility = Visibility.Collapsed;
            });
        }
        catch
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.Invoke(() => PreviewLoading.Text = "Preview unavailable");
        }
    }

    private void OnStatusChanged(MapStatus status, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateStatus(status, message);
            if (status == MapStatus.Success || status == MapStatus.Updating)
                LoadPreview();
        });
    }

    private void UpdateStatus(MapStatus status, string message)
    {
        StatusText.Text = message;
        if (_trayIcon != null)
            _trayIcon.Text = $"Overpass - {(message.Length > 50 ? message[..50] : message)}";

        if (_locationService != null)
        {
            string name = _locationService.CurrentLocationName ?? "";
            string coords = _locationService.HasLocation
                ? $"{_locationService.Latitude:F4}, {_locationService.Longitude:F4}" : "";
            LocationText.Text = !string.IsNullOrEmpty(name) ? $"{name} ({coords})" : coords;
        }
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool hasLoc = _locationService?.HasLocation == true;
        RefreshButton.IsEnabled = hasLoc;
        NewLocationButton.IsEnabled = hasLoc && _settings?.UseCurrentLocation == false;
        NewLocationButton.Visibility = _settings?.UseCurrentLocation == false ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_mapManager != null) await _mapManager.UpdateMapAsync(force: true);
    }

    private void NewLocation_Click(object sender, RoutedEventArgs e)
        => _locationService?.PickRandomLocation(_settings?.RandomLocationCategory ?? "");

    private void HideToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        if (!_hasShownTrayTip)
        {
            _hasShownTrayTip = true;
            _trayIcon?.ShowBalloonTip(2000, "Overpass",
                "Running in the system tray. Double-click the icon to show.", Forms.ToolTipIcon.Info);
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => ExitApp();

    private void ExitApp()
    {
        _isExiting = true;
        _locationService?.Stop();
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            if (!_hasShownTrayTip)
            {
                _hasShownTrayTip = true;
                _trayIcon?.ShowBalloonTip(2000, "Overpass",
                    "Running in the system tray. Double-click the icon to show.", Forms.ToolTipIcon.Info);
            }
        }
    }

    private static Icon LoadAppIcon()
    {
        // Try loading from embedded WPF resource
        try
        {
            var sri = System.Windows.Application.GetResourceStream(new Uri("satellite.ico", UriKind.Relative));
            if (sri != null)
            {
                using var stream = sri.Stream;
                return new Icon(stream, 16, 16);
            }
        }
        catch { }

        // Fallback: generated icon
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(60, 140, 220));
            g.FillEllipse(brush, 2, 2, 12, 12);
            using var pen = new Pen(Color.White, 1f);
            g.DrawArc(pen, 4, 5, 8, 6, 0, 180);
            g.DrawArc(pen, 5, 3, 6, 10, 270, 180);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void Hyperlink_Navigate(object sender, RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
