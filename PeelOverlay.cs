using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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

    public void Start()
    {
        if (_watch.IsRunning) return;
        _watch.Start();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
        InvalidateVisual();
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
        // Suppress Completed if we were torn out before finishing.
        _completedFired = true;
        Snapshot?.Dispose();
        Snapshot = null;
        base.OnDetachedFromVisualTree(e);
    }
}
