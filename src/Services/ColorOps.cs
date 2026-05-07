using System;
using Avalonia.Media;

namespace Stickies.Services;

internal static class ColorOps
{
    public static Color Darken(Color c, double factor) => Color.FromRgb(
        (byte)Math.Clamp(c.R * factor, 0, 255),
        (byte)Math.Clamp(c.G * factor, 0, 255),
        (byte)Math.Clamp(c.B * factor, 0, 255));

    public static Color DarkenInHsl(Color rgb, double percent)
    {
        double r = rgb.R / 255.0, g = rgb.G / 255.0, b = rgb.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-9)
        {
            h = 0; s = 0;
        }
        else
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = ((g - b) / d) + (g < b ? 6 : 0);
            else if (max == g) h = ((b - r) / d) + 2;
            else h = ((r - g) / d) + 4;
            h /= 6;
        }

        l = Math.Max(0, Math.Min(1, l - percent / 100.0));

        double r2, g2, b2;
        if (Math.Abs(s) < 1e-9)
        {
            r2 = g2 = b2 = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r2 = HueToRgb(p, q, h + 1.0 / 3.0);
            g2 = HueToRgb(p, q, h);
            b2 = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        return Color.FromArgb(rgb.A, (byte)(r2 * 255), (byte)(g2 * 255), (byte)(b2 * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
