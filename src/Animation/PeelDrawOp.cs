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
        // Linear gradient perpendicular to the hinge: darkest along the hypotenuse
        // (where the lifted flap is closest to the page beneath) and fading to
        // transparent at the BL corner (largest gap, softest shadow). This matches
        // the physical shadow geometry of a hinged-paper flap and reads as depth
        // rather than a soft blob.
        if (_shadowAlpha > 0)
        {
            canvas.Save();
            canvas.Translate((float)_bounds.X, (float)_bounds.Y + _cornerSize);

            byte alphaByte = (byte)Math.Clamp(_shadowAlpha * 55, 0, 55);
            using var shadowShader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),                                    // BL corner (dark)
                new SKPoint(_cornerSize * 0.5f, -_cornerSize * 0.5f), // hinge midpoint (transparent)
                new[] { new SKColor(0, 0, 0, alphaByte), new SKColor(0, 0, 0, 0) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            using var shadowPaint = new SKPaint
            {
                Shader = shadowShader,
                IsAntialias = true
            };

            // Clip to the flap region: lower-left half of overlay (the BL
            // corner triangle of the body), separated from the upper-right by
            // the TL-BR diagonal hinge.
            using var clip = new SKPath();
            clip.MoveTo(0, -_cornerSize);   // TL
            clip.LineTo(_cornerSize, 0);    // BR
            clip.LineTo(0, 0);              // BL (right angle, lifts)
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

        // Hinge = TL-BR diagonal; passes through (0,-cs) and (cs,0), not origin.
        // Translate so TL→origin, rotate around (1,1,0) axis, translate back, then
        // apply perspective. PostConcat(N) sets m = N × m, so calling them in the
        // desired application order builds m = perspective × fromOrigin × rot ×
        // toOrigin, which when applied to v produces v → toOrigin → rot →
        // fromOrigin → perspective — the correct hinge-rotation pipeline.
        var toOrigin = SKMatrix44.CreateTranslation(0, _cornerSize, 0);
        var fromOrigin = SKMatrix44.CreateTranslation(0, -_cornerSize, 0);
        var rot = SKMatrix44.CreateRotation(1, 1, 0,
            (float)(angleDeg * Math.PI / 180.0));

        var m = SKMatrix44.CreateIdentity();
        m.PostConcat(toOrigin);
        m.PostConcat(rot);
        m.PostConcat(fromOrigin);
        m.PostConcat(perspective);

        var m2d = m.Matrix;
        canvas.Concat(ref m2d);

        // Flap triangle: lower-left half of overlay. Right angle at BL (the
        // body's BL corner — the actual lifting corner). Hypotenuse runs
        // TL → BR (perpendicular to the BL→TR motion of the lifting corner).
        using var path = new SKPath();
        path.MoveTo(0, -_cornerSize);   // TL
        path.LineTo(_cornerSize, 0);    // BR
        path.LineTo(0, 0);              // BL (right angle, lifts)
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

            // Always darken the front face so the lifting flap is distinguishable
            // from the body color it's textured with — without this, the front face
            // is essentially invisible until ~90° edge-on, and the eye reads the
            // animation as "back face appears at TR" instead of "BL corner lifting
            // to TR." Base darkening at t=0; sin(angle) ramp on top peaks at 90°.
            double tiltMix = Math.Sin(angleDeg * Math.PI / 180.0);
            byte tiltAlpha = (byte)Math.Clamp(50 + tiltMix * 80, 0, 130);
            using var tiltPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, tiltAlpha),
                IsAntialias = true
            };
            canvas.DrawPath(path, tiltPaint);
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

        // Edge stroke around the flap silhouette. Drawn under the same matrix
        // so it follows the perspective. Heavier than the prior 1px/alpha-80
        // pass so the silhouette is unambiguous in every frame.
        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(0, 0, 0, 110),
            IsAntialias = true
        };
        canvas.DrawPath(path, strokePaint);

        canvas.Restore();
    }
}
