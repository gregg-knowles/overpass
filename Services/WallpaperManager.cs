using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Overpass.Services;

public static class WallpaperManager
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public static bool SetWallpaper(string filePath)
    {
        // Set wallpaper style to Span mode for multi-monitor support
        // WallpaperStyle=22 corresponds to Span, TileWallpaper=0 disables tiling
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
            key?.SetValue("WallpaperStyle", "22");
            key?.SetValue("TileWallpaper", "0");
        }
        catch { }

        int result = SystemParametersInfo(
            SPI_SETDESKWALLPAPER,
            0,
            filePath,
            SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        return result != 0;
    }

    public static async Task<bool> SetLockScreenImage(string filePath)
    {
        // Try WinRT LockScreen API first
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            await LockScreen.SetImageFileAsync(file);
            return true;
        }
        catch { }

        // Fallback: registry-based approach (requires elevation for HKLM)
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP", true);
            if (key != null)
            {
                key.SetValue("LockScreenImagePath", filePath);
                key.SetValue("LockScreenImageUrl", filePath);
                key.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }

        return false;
    }
}
