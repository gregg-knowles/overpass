using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SatelliteEyesWin.Services;

public static class ImageEffects
{
    public static Bitmap Apply(Bitmap source, string effectId)
    {
        return effectId switch
        {
            "darken" => Darken(source),
            "desaturate" => Desaturate(source),
            "darken-desaturate" => Darken(Desaturate(source)),
            "pixellate" => Pixellate(source, 8),
            "blur" => BoxBlur(source, 6),
            _ => new Bitmap(source)
        };
    }

    private static Bitmap Darken(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        // Draw original
        g.DrawImage(source, 0, 0);
        // Overlay semi-transparent black
        using var brush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
        g.FillRectangle(brush, 0, 0, source.Width, source.Height);
        return result;
    }

    private static Bitmap Desaturate(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);

        // Desaturation color matrix
        float[][] matrixItems = {
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        };
        var colorMatrix = new ColorMatrix(matrixItems);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(colorMatrix);
        g.DrawImage(source,
            new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, attrs);
        return result;
    }

    private static Bitmap Pixellate(Bitmap source, int blockSize)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        // Scale down then back up
        int smallW = Math.Max(1, source.Width / blockSize);
        int smallH = Math.Max(1, source.Height / blockSize);
        using var small = new Bitmap(source, smallW, smallH);
        g.DrawImage(small, 0, 0, source.Width, source.Height);
        return result;
    }

    private static Bitmap BoxBlur(Bitmap source, int radius)
    {
        // Simple two-pass box blur via downscale/upscale
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;

        int factor = radius;
        int smallW = Math.Max(1, source.Width / factor);
        int smallH = Math.Max(1, source.Height / factor);
        using var small = new Bitmap(source, smallW, smallH);
        g.DrawImage(small, 0, 0, source.Width, source.Height);
        return result;
    }
}
