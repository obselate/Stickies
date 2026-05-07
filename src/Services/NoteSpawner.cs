// NoteSpawner orchestrates creating a new sticky, optionally with a peel-from-source
// animation. This service is INTENTIONALLY view-aware: peel orchestration must drop
// a PeelOverlay into the source window's visual tree and capture a snapshot of the
// source's body. Per the 3mp spec's Q1=A decision we accept a MainWindow parameter
// directly rather than abstracting through an interface. New view-aware services
// should be rare and explicitly marked, like this one.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stickies.Animation;
using Stickies.Views;

namespace Stickies.Services;

internal static class NoteSpawner
{
    public static void SpawnNew(MainWindow? near)
    {
        if (near is null)
        {
            const int W = 280, H = 280;
            var reference = FindReferenceWindow();
            int? nx = null, ny = null;
            if (reference is not null)
            {
                var pos = PlacementService.FindAvailableSpace(null, reference.Position, W, H);
                nx = pos.X;
                ny = pos.Y;
            }
            var rowN = App.Store.Create(nx, ny, W, H);
            var wN = new MainWindow(rowN);
            Tween.FadeIn(wN);
            wN.Show();
            return;
        }

        if (near._isAnimating) return;
        near._isAnimating = true;

        int srcW = (int)near.Width;
        int srcH = (int)near.Height;
        Color bodyColor;
        try { bodyColor = Color.Parse(near._color); }
        catch { bodyColor = Color.FromRgb(0xFF, 0xF5, 0x9E); }

        Bitmap snapshot;
        try
        {
            snapshot = CaptureCornerSnapshot(near);
        }
        catch
        {
            near._isAnimating = false;
            SpawnReplacement(near, srcW, srcH);
            return;
        }

        var overlay = new PeelOverlay { Snapshot = snapshot, BodyColor = bodyColor };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        overlay.HorizontalAlignment = HorizontalAlignment.Left;
        overlay.VerticalAlignment = VerticalAlignment.Bottom;
        overlay.Width = PeelOverlay.CornerSize;
        overlay.Height = PeelOverlay.CornerSize;
        overlay.IsHitTestVisible = false;

        overlay.Completed += () =>
        {
            near.BodyGrid.Children.Remove(overlay);
            near._isAnimating = false;
            SpawnReplacement(near, srcW, srcH);
        };

        near.BodyGrid.Children.Add(overlay);
        overlay.Start();
    }

    private static void SpawnReplacement(MainWindow source, int width, int height)
    {
        var preferred = source.Position;
        var newNotePos = PlacementService.OverlapsAnyOther(source, preferred, width, height)
            ? PlacementService.FindAvailableSpace(source, preferred, width, height)
            : preferred;
        var oldNewPos = PlacementService.FindAvailableSpace(source, newNotePos, width, height);

        Tween.AnimateMove(source, oldNewPos);

        var row = App.Store.Create(newNotePos.X, newNotePos.Y, width, height);
        var fresh = new MainWindow(row);
        fresh.Opacity = 0;
        fresh.Show();
        Tween.FadeIn(fresh, durationMs: 200);
    }

    private static MainWindow? FindReferenceWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        foreach (var w in desktop.Windows)
            if (w is MainWindow mw && mw.IsVisible) return mw;
        return null;
    }

    internal static Bitmap CaptureCornerSnapshot(MainWindow source)
    {
        var body = source.BodyBorder;
        var bw = (int)System.Math.Ceiling(body.Bounds.Width);
        var bh = (int)System.Math.Ceiling(body.Bounds.Height);
        if (bw <= 0 || bh <= 0)
            return new RenderTargetBitmap(new PixelSize(PeelOverlay.CornerSize, PeelOverlay.CornerSize));

        var full = new RenderTargetBitmap(new PixelSize(bw, bh));
        full.Render(body);

        int cs = PeelOverlay.CornerSize;
        int cropW = System.Math.Min(cs, bw);
        int cropH = System.Math.Min(cs, bh);
        int srcX = 0;
        int srcY = bh - cropH;

        var cropped = new RenderTargetBitmap(new PixelSize(cs, cs));
        using (var ctx = cropped.CreateDrawingContext())
        {
            int dstY = cs - cropH;
            ctx.DrawImage(
                full,
                new Rect(srcX, srcY, cropW, cropH),
                new Rect(0, dstY, cropW, cropH));
        }
        full.Dispose();
        return cropped;
    }
}
