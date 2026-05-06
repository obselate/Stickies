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
    public const double DurationMs = 700.0;
    public const int CornerSize = 110;

    public Bitmap? Snapshot { get; set; }

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
        double t = Math.Clamp(_watch.Elapsed.TotalMilliseconds / DurationMs, 0.0, 1.0);
        context.Custom(new PeelDrawOp(bounds, tex, t, cornerSize: CornerSize));
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
