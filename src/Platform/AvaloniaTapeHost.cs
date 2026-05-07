using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Stickies.Platform;

// Mac + Linux implementation. Both platforms ride Avalonia defaults (Metal/OpenGl
// on Mac, Egl on Linux), so TransparencyLevelHint=Transparent actually shows
// desktop through uncovered pixels — no Win32-style RedirectionSurface
// black-backbuffer to work around.
//
// The tape lives in its own owned Avalonia Window so the parent note doesn't
// have to grow upward (no LockBuffer, no DB-vs-window-Y bookkeeping). Owner
// relationship via Show(owner) means the tape z-orders with its note and
// follows minimize/hide/alt-tab.
internal sealed class AvaloniaTapeHost : ITapeHost
{
    // Tape geometry: 22px tall, inset 24px from each side of the note, sits
    // half-above / half-on the note's top edge. Pad scales with tape width
    // because rotation (-1.5°) extends the vertical AABB by sin(angle) * width.
    private const double RotationDegrees = 1.5;
    private const int TapeHeight = 22;
    private const int TapeInset = 24;

    // Pad needed (in DIPs) to keep the rotated tape inside the host window.
    private static int RequiredPad(double tapeWidthDips)
    {
        var sin = Math.Sin(RotationDegrees * Math.PI / 180);
        return (int)Math.Ceiling(tapeWidthDips * sin / 2) + 2;
    }

    private readonly Window _owner;
    private readonly Window _wnd;
    private readonly Border _tape;
    private bool _opened;
    private (PixelRect Bounds, double Scale)? _pending;
    private bool _disposed;

    public AvaloniaTapeHost(Window owner)
    {
        _owner = owner;

        _tape = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#80FAFAFA"), 0),
                    new GradientStop(Color.Parse("#80ECECEC"), 0.5),
                    new GradientStop(Color.Parse("#80FAFAFA"), 1),
                },
            },
            RenderTransform = new RotateTransform(-RotationDegrees),
            IsHitTestVisible = false,
        };

        _wnd = new Window
        {
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Topmost = false,
            Content = _tape,
            Title = "Stickies.Tape",
        };
    }

    public void Show()
    {
        if (_disposed) return;
        if (!_opened)
        {
            if (_pending is { } p) ApplyState(p.Bounds, p.Scale);
            _wnd.Show(_owner);
            _opened = true;
        }
        else if (!_wnd.IsVisible)
        {
            _wnd.Show();
        }
    }

    public void Hide()
    {
        if (_disposed || !_opened) return;
        if (_wnd.IsVisible) _wnd.Hide();
    }

    public void Update(PixelRect noteBounds, double scale)
    {
        if (_disposed) return;
        if (!_opened) { _pending = (noteBounds, scale); return; }
        ApplyState(noteBounds, scale);
    }

    private void ApplyState(PixelRect noteBounds, double scale)
    {
        // Avalonia Window sizes are in DIPs, but Position is in device pixels.
        // Tape geometry constants are DIPs; pad scales with tape width because
        // rotation extends the AABB linearly with width.
        double logicalNoteWidth = noteBounds.Width / scale;
        double tapeWidthDips = logicalNoteWidth - 2 * TapeInset;
        int pad = RequiredPad(tapeWidthDips);

        _tape.Margin = new Thickness(pad);
        _wnd.Width = tapeWidthDips + 2 * pad;
        _wnd.Height = TapeHeight + 2 * pad;

        int physInset = (int)Math.Round(TapeInset * scale);
        int physPad = (int)Math.Round(pad * scale);
        int physTapeHeight = (int)Math.Round(TapeHeight * scale);
        _wnd.Position = new PixelPoint(
            noteBounds.X + physInset - physPad,
            noteBounds.Y - physTapeHeight / 2 - physPad);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _wnd.Close(); } catch { }
    }
}
