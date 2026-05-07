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

    public static (double H, double S, double V) RgbToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double v = max;
        double s = max < 1e-9 ? 0 : d / max;
        double h;
        if (d < 1e-9) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
        return (h, s, v);
    }

    public static Color HsvToRgb(double h, double s, double v)
    {
        if (s < 1e-9)
        {
            byte g = (byte)Math.Clamp(v * 255, 0, 255);
            return Color.FromRgb(g, g, g);
        }
        h = (h % 360 + 360) % 360 / 60;
        int i = (int)Math.Floor(h);
        double f = h - i;
        double p = v * (1 - s);
        double q = v * (1 - s * f);
        double t = v * (1 - s * (1 - f));
        double r2, g2, b2;
        switch (i)
        {
            case 0: r2 = v; g2 = t; b2 = p; break;
            case 1: r2 = q; g2 = v; b2 = p; break;
            case 2: r2 = p; g2 = v; b2 = t; break;
            case 3: r2 = p; g2 = q; b2 = v; break;
            case 4: r2 = t; g2 = p; b2 = v; break;
            default: r2 = v; g2 = p; b2 = q; break;
        }
        return Color.FromRgb(
            (byte)Math.Clamp(r2 * 255, 0, 255),
            (byte)Math.Clamp(g2 * 255, 0, 255),
            (byte)Math.Clamp(b2 * 255, 0, 255));
    }
}
