using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Stickies;

internal sealed class PeelOverlay : Control
{
    public const double DurationMs = 300.0;
    public const int CornerSize = 110;
    private static readonly CubicBezier Easing = new(0.3, 0, 0.2, 1);

    public Bitmap? Snapshot { get; set; }

    public Avalonia.Media.Color BodyColor { get; set; } = Avalonia.Media.Color.FromRgb(0xFF, 0xF5, 0x9E);

    public event Action? Completed;

    private readonly Stopwatch _watch = new();
    private DispatcherTimer? _timer;
    private bool _completedFired;
    private SKBitmap? _skSnapshot;

    public void Start()
    {
        if (_watch.IsRunning) return;
        _watch.Start();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
        InvalidateVisual();
    }

    private SKBitmap? GetOrDecodeSkBitmap()
    {
        if (_skSnapshot is not null) return _skSnapshot;
        if (Snapshot is null) return null;

        using var ms = new MemoryStream();
        Snapshot.Save(ms);
        ms.Position = 0;
        _skSnapshot = SKBitmap.Decode(ms);
        return _skSnapshot;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var tex = GetOrDecodeSkBitmap();
        double elapsedMs = _watch.Elapsed.TotalMilliseconds;
        double rawT = Math.Clamp(elapsedMs / DurationMs, 0.0, 1.0);
        double easedT = Easing.Ease(rawT);

        // Bell curve: shadow peaks mid-animation (when the flap is highest off
        // the page) and fades to zero by the end. Avoids the prior behavior
        // where the end frame was dominated by a fully-faded-in shadow blob.
        double shadowAlpha = Math.Sin(rawT * Math.PI);

        context.Custom(new PeelDrawOp(bounds, tex, easedT, shadowAlpha, cornerSize: CornerSize, backFaceColor: ComputeBackFaceColor()));
    }

    // Mix body color with white at 20/80 — push hard toward white so the paper
    // underside is unmistakably distinct from the colored front, even when the
    // body color is already light (yellow, pink, light blue).
    private SKColor ComputeBackFaceColor()
    {
        byte r = (byte)(BodyColor.R * 0.2 + 255 * 0.8);
        byte g = (byte)(BodyColor.G * 0.2 + 255 * 0.8);
        byte b = (byte)(BodyColor.B * 0.2 + 255 * 0.8);
        return new SKColor(r, g, b, 255);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (_watch.Elapsed.TotalMilliseconds >= DurationMs && !_completedFired)
        {
            _completedFired = true;
            StopInternal();
            Completed?.Invoke();
        }
    }

    private void StopInternal()
    {
        _watch.Stop();
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StopInternal();
        _completedFired = true;
        _skSnapshot?.Dispose();
        _skSnapshot = null;
        Snapshot?.Dispose();
        Snapshot = null;
        base.OnDetachedFromVisualTree(e);
    }
}
