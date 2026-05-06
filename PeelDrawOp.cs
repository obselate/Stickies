using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Stickies;

internal sealed class PeelDrawOp : ICustomDrawOperation
{
    private readonly Rect _bounds;
    private readonly SKBitmap? _texture;
    private readonly double _t;       // eased, 0..1
    private readonly int _cornerSize;

    public PeelDrawOp(Rect bounds, SKBitmap? texture, double t, int cornerSize)
    {
        _bounds = bounds;
        _texture = texture;
        _t = t;
        _cornerSize = cornerSize;
    }

    public Rect Bounds => _bounds;
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { /* texture is owned by caller */ }

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null) return;
        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        canvas.Save();

        // Origin is overlay's top-left in screen DIPs. Move to overlay's BL,
        // because the corner-area math is anchored at (0,0) = overlay BL.
        canvas.Translate((float)_bounds.X, (float)_bounds.Y + _cornerSize);

        // Front-face triangle: polygon(0 0, 100% 100%, 0 100%) within the
        // CornerSize box. Anchored at overlay BL means y is negative-up,
        // so we'll flip y-axis convention here.
        // After Translate above, (0,0) = BL of overlay region, x→right, y→down.
        // CornerSize box occupies (0, -cornerSize) to (cornerSize, 0) in this frame.

        using var path = new SKPath();
        path.MoveTo(0, -_cornerSize);                     // TL of corner-area
        path.LineTo(_cornerSize, 0);                      // BR of corner-area
        path.LineTo(0, 0);                                // BL of corner-area (right angle)
        path.Close();

        if (_texture is not null)
        {
            // Map the texture so its BL aligns with the triangle's BL,
            // texture extends rightward and upward over the cornerSize box.
            using var shader = SKShader.CreateBitmap(
                _texture,
                SKShaderTileMode.Clamp,
                SKShaderTileMode.Clamp,
                SKMatrix.CreateTranslation(0, -_cornerSize));
            using var paint = new SKPaint
            {
                Shader = shader,
                IsAntialias = true
            };
            canvas.DrawPath(path, paint);
        }
        else
        {
            using var fallback = new SKPaint
            {
                Color = new SKColor(0xFF, 0xF5, 0x9E, 0xFF),
                IsAntialias = true
            };
            canvas.DrawPath(path, fallback);
        }

        canvas.Restore();
    }
}
