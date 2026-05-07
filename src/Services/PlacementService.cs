// Window-placement search. Owns the spiral-out algorithm that finds a non-overlapping
// spot near a reference window, the overlap test against open windows, and the
// "is this remembered position still on a screen?" check used at note load time.
//
// Pragmatic exception (Q1=A in 3mp spec): these methods accept MainWindow directly
// rather than going through an interface. There is exactly one window class in this
// app and no test suite — abstracting the parameter type would add plumbing without
// enabling reuse or testability.

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace Stickies.Services;

internal static class PlacementService
{
    public static bool IsOnAnyScreen(IReadOnlyList<Screen>? screens, int x, int y, int w, int h)
    {
        if (screens is null || screens.Count == 0) return true;
        foreach (var s in screens)
        {
            var b = s.Bounds;
            if (x + w > b.X && x < b.X + b.Width && y + h > b.Y && y < b.Y + b.Height)
                return true;
        }
        return false;
    }

    public static PixelRect PhysicalRect(MainWindow mw)
    {
        double s = mw.RenderScaling > 0 ? mw.RenderScaling : 1.0;
        return new PixelRect(mw.Position, new PixelSize((int)(mw.Width * s), (int)(mw.Height * s)));
    }

    public static bool OverlapsAnyOther(MainWindow source, PixelPoint pos, int width, int height)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;
        double srcScale = source.RenderScaling > 0 ? source.RenderScaling : 1.0;
        var rect = new PixelRect(pos, new PixelSize((int)(width * srcScale), (int)(height * srcScale)));
        foreach (var w in desktop.Windows)
        {
            if (w is MainWindow mw && mw != source && mw.IsVisible)
            {
                if (rect.Intersects(PhysicalRect(mw))) return true;
            }
        }
        return false;
    }

    public static PixelPoint FindAvailableSpace(
        MainWindow? source,
        PixelPoint origin,
        int width,
        int height)
    {
        const int marginDip = 40;
        const int gapDip = 12;

        MainWindow? screenAnchor = source;
        var others = new List<PixelRect>();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows)
            {
                if (w is MainWindow mw && mw != source && mw.IsVisible)
                {
                    others.Add(PhysicalRect(mw));
                    screenAnchor ??= mw;
                }
            }
        }

        double scale = screenAnchor?.RenderScaling > 0 ? screenAnchor.RenderScaling : 1.0;
        int wPx = (int)(width * scale);
        int hPx = (int)(height * scale);
        int marginPx = (int)(marginDip * scale);
        int gapPx = (int)(gapDip * scale);

        others.Add(new PixelRect(origin, new PixelSize(wPx, hPx)));

        var screen = screenAnchor?.Screens?.ScreenFromWindow(screenAnchor);
        var screenArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        var directions = new (int dx, int dy)[]
        {
            (1, 0), (0, 1), (-1, 0), (0, -1),
            (1, 1), (-1, 1), (1, -1), (-1, -1),
        };

        for (int dist = 1; dist <= 5; dist++)
        {
            foreach (var (dx, dy) in directions)
            {
                int nx = origin.X + dx * (wPx + gapPx) * dist;
                int ny = origin.Y + dy * (hPx + gapPx) * dist;

                if (nx < screenArea.X + marginPx) continue;
                if (ny < screenArea.Y + marginPx) continue;
                if (nx + wPx > screenArea.X + screenArea.Width - marginPx) continue;
                if (ny + hPx > screenArea.Y + screenArea.Height - marginPx) continue;

                var candidate = new PixelRect(nx, ny, wPx, hPx);
                bool overlap = false;
                foreach (var other in others)
                {
                    if (candidate.Intersects(other)) { overlap = true; break; }
                }
                if (overlap) continue;

                return new PixelPoint(nx, ny);
            }
        }

        int fxMin = screenArea.X + marginPx;
        int fxMax = screenArea.X + screenArea.Width - wPx - marginPx;
        int fyMin = screenArea.Y + marginPx;
        int fyMax = screenArea.Y + screenArea.Height - hPx - marginPx;
        int fx = System.Math.Clamp(origin.X + gapPx, System.Math.Min(fxMin, fxMax), System.Math.Max(fxMin, fxMax));
        int fy = System.Math.Clamp(origin.Y + gapPx, System.Math.Min(fyMin, fyMax), System.Math.Max(fyMin, fyMax));
        return new PixelPoint(fx, fy);
    }
}
