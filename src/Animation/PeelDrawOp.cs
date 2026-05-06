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
    private readonly double _t;           // eased, 0..1
    private readonly double _shadowAlpha; // 0..1, fades in independently
    private readonly int _cornerSize;
    private readonly SKColor _backFaceColor;

    public PeelDrawOp(Rect bounds, SKBitmap? texture, double t, double shadowAlpha, int cornerSize, SKColor backFaceColor)
    {
        _bounds = bounds;
        _texture = texture;
        _t = t;
        _shadowAlpha = shadowAlpha;
        _cornerSize = cornerSize;
        _backFaceColor = backFaceColor;
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

        // ===== Shadow pass (un-rotated, in screen frame) =====
        if (_shadowAlpha > 0)
        {
            canvas.Save();
            canvas.Translate((float)_bounds.X, (float)_bounds.Y + _cornerSize);

            // Shadow center: roughly at the centroid of the original triangle,
            // pushed slightly outward along the hinge-perpendicular direction
            // so it suggests the flap lifting away.
            float cx = _cornerSize * 0.33f;
            float cy = -_cornerSize * 0.33f;
            float radius = _cornerSize * 0.7f;

            byte alphaByte = (byte)Math.Clamp(_shadowAlpha * 90, 0, 90); // max ~35% black
            using var shadowShader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                radius,
                new[] { new SKColor(0, 0, 0, alphaByte), new SKColor(0, 0, 0, 0) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            using var shadowPaint = new SKPaint
            {
                Shader = shadowShader,
                IsAntialias = true
            };

            // Clip to the original triangle so the shadow doesn't leak
            // outside the corner-area (the page-beneath only exists where
            // the flap was).
            using var clip = new SKPath();
            clip.MoveTo(0, -_cornerSize);
            clip.LineTo(_cornerSize, 0);
            clip.LineTo(0, 0);
            clip.Close();
            canvas.ClipPath(clip, antialias: true);
            canvas.DrawPaint(shadowPaint);

            canvas.Restore();
        }

        // ===== Rotated face pass =====
        canvas.Save();
        canvas.Translate((float)_bounds.X, (float)_bounds.Y + _cornerSize);

        float angleDeg = (float)(180.0 * _t);

        // Perspective 600px: M[3,2] = -1/600.
        var perspective = SKMatrix44.CreateIdentity();
        perspective[3, 2] = -1f / 600f;

        // Hinge origin = TL of corner-area = (0, -cornerSize) in our translated frame.
        var toOrigin = SKMatrix44.CreateTranslation(0, _cornerSize, 0);
        var fromOrigin = SKMatrix44.CreateTranslation(0, -_cornerSize, 0);

        // Rotate around axis (1, 1, 0) by 180·t degrees.
        var rot = SKMatrix44.CreateRotation(1, 1, 0,
            (float)(angleDeg * Math.PI / 180.0));

        // Compose: M = perspective × fromOrigin × rot × toOrigin (right-multiply order).
        var m = SKMatrix44.CreateIdentity();
        m.PostConcat(perspective);
        m.PostConcat(fromOrigin);
        m.PostConcat(rot);
        m.PostConcat(toOrigin);

        // Apply 4x4 to 2D canvas: extract 3x3 with perspective row preserved.
        var m2d = m.Matrix;
        canvas.Concat(ref m2d);

        // Front-face triangle.
        using var path = new SKPath();
        path.MoveTo(0, -_cornerSize);     // TL
        path.LineTo(_cornerSize, 0);      // BR
        path.LineTo(0, 0);                // BL (right angle)
        path.Close();

        bool frontVisible = _t < 0.5;

        if (frontVisible && _texture is not null)
        {
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
        else if (!frontVisible)
        {
            using var paint = new SKPaint
            {
                Color = _backFaceColor,
                IsAntialias = true
            };
            canvas.DrawPath(path, paint);
        }

        // Edge stroke around the flap silhouette so the lifting paper reads as a
        // distinct shape against the body color (especially when source and back
        // face are similar). Drawn under the same matrix so it follows perspective.
        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.0f,
            Color = new SKColor(0, 0, 0, 80),
            IsAntialias = true
        };
        canvas.DrawPath(path, strokePaint);

        canvas.Restore();
    }
}
