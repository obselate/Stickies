using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Stickies.Animation;

internal static class Tween
{
    public static void AnimateMove(Window w, PixelPoint to, int durationMs = 200)
    {
        var from = w.Position;
        if (from == to) return;
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
            double eased = 1 - Math.Pow(1 - t, 3);
            int x = (int)(from.X + (to.X - from.X) * eased);
            int y = (int)(from.Y + (to.Y - from.Y) * eased);
            w.Position = new PixelPoint(x, y);
            if (t >= 1.0)
            {
                w.Position = to;
                timer.Stop();
                sw.Stop();
            }
        };
        timer.Start();
    }

    public static void FadeIn(Window w, int durationMs = 50)
    {
        w.Opacity = 0;
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
            w.Opacity = t;
            if (t >= 1.0)
            {
                timer.Stop();
                sw.Stop();
            }
        };
        timer.Start();
    }
}
