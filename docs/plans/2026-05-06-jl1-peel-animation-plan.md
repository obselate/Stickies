# 2.5D Peel Animation Implementation Plan (Stickies-jl1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the 700ms 2.5D peel animation on the source note's bottom-left corner when a new note is spawned, per [docs/specs/2026-05-06-jl1-peel-animation-design.md](../specs/2026-05-06-jl1-peel-animation-design.md).

**Architecture:** Custom Avalonia `Control` (`PeelOverlay`) inserted at runtime as a child of the source note's body Grid. Renders via Skia by issuing an `ICustomDrawOperation` that leases `ISkiaSharpApiLeaseFeature` and draws a perspective-transformed front-face (texture-mapped from a pre-animation snapshot of the BL 110×110 region) and back-face (filled `#c9b853`). Stopwatch-driven 0→1 timeline, cubic-bezier easing, fires `Completed` event when done. `MainWindow.SpawnNew(near)` orchestrates: snapshot → attach overlay → on `Completed` spawn new note window at `near.Position` (no cascade).

**Tech Stack:** Avalonia 11.3 (Skia software renderer), SkiaSharp 2.88.x (transitive via Avalonia.Skia), .NET 9 NativeAOT, Stopwatch + DispatcherTimer for animation loop.

**Project conventions:**
- Manual testing only (no test framework). Each task ends with manual verification steps from the spec's test cases.
- Beads tracker: bead `Stickies-jl1` is the rolled-up tracker. Plan's checkboxes are the granular tracker.
- One commit per task. Atomic.
- Build: `dotnet build -c Debug` for iteration. AOT publish only at Task 10.
- Run: `bin/Debug/net9.0/Stickies.exe` from project root.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `PeelOverlay.cs` | CREATE | Custom `Control` rendering the peel via Skia. Owns animation timeline, snapshot, `Completed` event. |
| `PeelDrawOp.cs` | CREATE | `ICustomDrawOperation` that does the actual Skia drawing per frame. Stateless wrt the control; receives snapshot + eased-t per frame. |
| `CubicBezier.cs` | CREATE | Tiny easing helper — solves cubic-bezier(0.5, 0, 0.4, 1) for arbitrary t. ~30 lines. |
| `MainWindow.axaml.cs` | MODIFY | Modify `SpawnNew(near)`: snapshot, attach overlay, await `Completed` before spawning B at `near.Position`. Remove cascade offset. Add per-window `_isAnimating` flag. Add null-source fade-in. |
| `MainWindow.axaml` | MODIFY (minor) | Add `x:Name="BodyGrid"` to the inner `Grid` so we can address it from code without a cast. |
| `Stickies.csproj` | MODIFY (conditional) | Add explicit `<PackageReference Include="SkiaSharp" Version="2.88.*" />` if SkiaSharp types are not transitively visible. Verified in Task 1. |

---

## Task 1: Verify SkiaSharp transitive accessibility

**Goal:** Determine whether we need an explicit `SkiaSharp` package reference or if Avalonia exposes the types we need transitively. This is a 5-minute spike.

**Files:**
- Modify (temporary): create a throwaway file `_SkiaSpike.cs` with a single using
- Modify (conditional): `Stickies.csproj` — add SkiaSharp package reference if needed

- [ ] **Step 1: Create temporary spike file**

Create `_SkiaSpike.cs` with this content:

```csharp
using SkiaSharp;
using Avalonia.Skia;
using Avalonia.Rendering.SceneGraph;

namespace Stickies;

internal static class _SkiaSpike
{
    public static void Touch()
    {
        SKMatrix44 m = SKMatrix44.CreateIdentity();
        SKCanvas? c = null;
        ISkiaSharpApiLeaseFeature? f = null;
        ICustomDrawOperation? op = null;
        _ = m; _ = c; _ = f; _ = op;
    }
}
```

- [ ] **Step 2: Build and observe**

Run: `dotnet build -c Debug`

Expected outcomes:
- **PASS** (build succeeds): Skia types are transitively visible. Skip Step 3.
- **FAIL** (CS0246: type or namespace not found): Skia is not visible without an explicit reference. Continue to Step 3.

- [ ] **Step 3 (only if Step 2 failed): Add explicit SkiaSharp reference**

Edit `Stickies.csproj`. Inside the `<ItemGroup>` containing the existing `PackageReference` items, add:

```xml
<PackageReference Include="SkiaSharp" Version="2.88.*" />
```

Run `dotnet build -c Debug` — must succeed.

- [ ] **Step 4: Delete the spike file**

Delete `_SkiaSpike.cs`.

Run: `dotnet build -c Debug` — must still succeed.

- [ ] **Step 5: Commit (only if csproj changed)**

If `Stickies.csproj` was modified:

```bash
git add Stickies.csproj
git commit -m "build: add explicit SkiaSharp ref for direct managed-API use (Stickies-jl1)"
```

If csproj was NOT modified, no commit — proceed to Task 2.

---

## Task 2: Scaffold PeelOverlay (no rendering yet)

**Goal:** Create the `PeelOverlay` class as a bare `Control` subclass with constants, properties, and lifecycle scaffolding. No rendering. Verify it compiles and can be instantiated.

**Files:**
- Create: `PeelOverlay.cs`

- [ ] **Step 1: Create PeelOverlay.cs**

Create `PeelOverlay.cs`:

```csharp
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
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build -c Debug`

Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add PeelOverlay.cs
git commit -m "feat: PeelOverlay scaffold — control + animation lifecycle (Stickies-jl1)"
```

---

## Task 3: Wire BodyGrid name in XAML

**Goal:** Give the inner Grid an `x:Name` so we can reach it from code without a `(Grid)BodyBorder.Child` cast.

**Files:**
- Modify: `MainWindow.axaml`

- [ ] **Step 1: Add x:Name to the Grid**

Edit `MainWindow.axaml`. Find the line:

```xml
        <Grid RowDefinitions="22,*">
```

Replace with:

```xml
        <Grid x:Name="BodyGrid" RowDefinitions="22,*">
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build -c Debug`

Expected: PASS. The generated `InitializeComponent` will now expose `BodyGrid` as a field on `MainWindow`.

- [ ] **Step 3: Commit**

```bash
git add MainWindow.axaml
git commit -m "chore: name BodyGrid for runtime overlay attachment (Stickies-jl1)"
```

---

## Task 4: Snapshot capture helper

**Goal:** Add a static helper that captures the bottom-left 110×110 region of a `MainWindow`'s body as an Avalonia `Bitmap` suitable for use as `PeelOverlay.Snapshot`.

**Files:**
- Modify: `MainWindow.axaml.cs` — add static `CaptureCornerSnapshot(MainWindow source)` method

- [ ] **Step 1: Add capture helper**

Edit `MainWindow.axaml.cs`. Just before the closing brace of the class (after `SpawnNew`), add:

```csharp
    internal static Bitmap CaptureCornerSnapshot(MainWindow source)
    {
        // Capture the entire BodyBorder, then crop to BL 110x110.
        var body = source.BodyBorder;
        var bw = (int)Math.Ceiling(body.Bounds.Width);
        var bh = (int)Math.Ceiling(body.Bounds.Height);
        if (bw <= 0 || bh <= 0)
            return new RenderTargetBitmap(new PixelSize(PeelOverlay.CornerSize, PeelOverlay.CornerSize));

        var full = new RenderTargetBitmap(new PixelSize(bw, bh));
        full.Render(body);

        // Crop to the bottom-left CornerSize x CornerSize.
        int cs = PeelOverlay.CornerSize;
        int cropW = Math.Min(cs, bw);
        int cropH = Math.Min(cs, bh);
        int srcX = 0;                  // BL x = 0
        int srcY = bh - cropH;         // BL y = bottom of body

        var cropped = new RenderTargetBitmap(new PixelSize(cs, cs));
        using (var ctx = cropped.CreateDrawingContext())
        {
            // Fill with transparent (RenderTargetBitmap default is transparent).
            // Draw the cropped region of `full` into the BL of `cropped`.
            int dstY = cs - cropH;     // anchor crop at BL of overlay region
            ctx.DrawImage(
                full,
                new Rect(srcX, srcY, cropW, cropH),
                new Rect(0, dstY, cropW, cropH));
        }
        full.Dispose();
        return cropped;
    }
```

Add the necessary `using` statements at the top of the file (if not already present):

```csharp
using Avalonia.Media.Imaging;
using Avalonia.Platform;
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build -c Debug`

Expected: PASS, 0 errors. (May need to also add `using Avalonia;` for `PixelSize` and `Rect` — usually already there via existing usings.)

- [ ] **Step 3: Manual smoke test**

We don't have a test framework. To smoke-test the capture, temporarily add this in `MainWindow.OnKeyDown` for the Ctrl+N branch (we'll remove it next task):

```csharp
        if (e.Key == Key.N)
        {
            // TEMP smoke test:
            var snap = CaptureCornerSnapshot(this);
            snap.Save("snap-debug.png");
            snap.Dispose();
            // SpawnNew(this);  // commented out for this test
            e.Handled = true;
            return;
        }
```

Run `dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe`. Press Ctrl+N. Open `snap-debug.png` — should show the BL 110×110 region of the note's body (yellow, with any text in that region, clipped at the top to transparent if note is shorter than 110px tall).

- [ ] **Step 4: Revert the smoke test code**

Remove the temp lines, restore `SpawnNew(this)` and the `e.Handled = true` line. Delete `snap-debug.png`.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.axaml.cs
git commit -m "feat: CaptureCornerSnapshot helper — RenderTargetBitmap of BL corner (Stickies-jl1)"
```

---

## Task 5: PeelDrawOp + first Skia render (static front-face triangle)

**Goal:** Create the `ICustomDrawOperation` that does the Skia work, and wire `PeelOverlay.Render` to dispatch it. First milestone: draw a STATIC front-face triangle textured from the snapshot. No rotation, no back-face yet. Proves the Skia path works.

**Files:**
- Create: `PeelDrawOp.cs`
- Modify: `PeelOverlay.cs` — override `Render`, dispatch `PeelDrawOp`

- [ ] **Step 1: Create PeelDrawOp.cs**

Create `PeelDrawOp.cs`:

```csharp
using System;
using System.IO;
using Avalonia;
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
```

- [ ] **Step 2: Modify PeelOverlay to override Render and convert Bitmap → SKBitmap**

Edit `PeelOverlay.cs`. Add these `using` statements at the top:

```csharp
using System.IO;
using Avalonia;
using Avalonia.Media;
using SkiaSharp;
```

Add a private field for the cached SKBitmap:

```csharp
    private SKBitmap? _skSnapshot;
```

Add a method to lazily decode the snapshot, and the `Render` override. Place these right after `Start()`:

```csharp
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
        // For Task 5, t is hardcoded to 0 — we draw the static front-face only.
        context.Custom(new PeelDrawOp(bounds, tex, t: 0.0, cornerSize: CornerSize));
    }
```

Update `OnDetachedFromVisualTree` to dispose the SKBitmap:

```csharp
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
```

- [ ] **Step 3: Temporarily wire it for visual smoke test**

Edit `MainWindow.axaml.cs`. In `OnKeyDown` for the Ctrl+N branch, replace `SpawnNew(this);` temporarily with:

```csharp
            if (e.Key == Key.N)
            {
                // TEMP smoke test for Task 5:
                var snap = CaptureCornerSnapshot(this);
                var overlay = new PeelOverlay { Snapshot = snap };
                Grid.SetRow(overlay, 0);
                Grid.SetRowSpan(overlay, 2);
                overlay.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                overlay.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                overlay.Width = PeelOverlay.CornerSize;
                overlay.Height = PeelOverlay.CornerSize;
                overlay.IsHitTestVisible = false;
                BodyGrid.Children.Add(overlay);
                overlay.Start();
                // SpawnNew(this);  // disabled during this test
                e.Handled = true;
                return;
            }
```

- [ ] **Step 4: Build and visually verify**

Run: `dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe`

Type some text in the note. Press Ctrl+N. **Expected:** the BL 110×110 region of the note now shows a triangle (BL 110×110 area's `polygon(0 0, 100% 100%, 0 100%)` — i.e., the triangle's hypotenuse runs from TL of the corner-area to BR, the right angle is at BL). The triangle's content matches the underlying yellow note in that region. The overlay sits on top of the body but the triangle's pixels match what's beneath, so visually it should look identical to the un-overlaid note. To confirm the overlay is actually rendering, temporarily change `_texture is not null` branch's `IsAntialias = true` to also set `Color = new SKColor(0xFF, 0x00, 0x00, 0x80)` on the paint — should turn the triangle red-tinted. Revert after confirming.

If the triangle does not render at all: check the `ISkiaSharpApiLeaseFeature` lease succeeded (debugger). If `leaseFeature is null`, the renderer isn't Skia — verify `Win32PlatformOptions.RenderingMode = [Software]` is set in `Program.cs`.

- [ ] **Step 5: Revert the temp wiring**

Remove the temp Ctrl+N block from Step 3. Restore `SpawnNew(this);` and `e.Handled = true;`.

Run: `dotnet build -c Debug` — must pass.

- [ ] **Step 6: Commit**

```bash
git add PeelDrawOp.cs PeelOverlay.cs
git commit -m "feat: Skia draw op renders textured front-face triangle (Stickies-jl1)"
```

---

## Task 6: Add back-face triangle and rotation animation

**Goal:** Apply `SKMatrix44` perspective × rotation around the diagonal hinge axis, animate t from 0→1 (linear for now), and draw both front-face (textured) and back-face (filled `#c9b853`) with backface-visibility correct (only one is visible at a time).

**Files:**
- Modify: `PeelDrawOp.cs` — add 3D matrix, draw both faces
- Modify: `PeelOverlay.cs` — pass live t (linear) instead of hardcoded 0

- [ ] **Step 1: Update PeelDrawOp.Render with 3D rotation + back-face**

Replace the entire `Render` method in `PeelDrawOp.cs` with:

```csharp
    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null) return;
        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;

        canvas.Save();
        // Translate to overlay's BL in device DIPs.
        canvas.Translate((float)_bounds.X, (float)_bounds.Y + _cornerSize);

        // Build the 3D matrix:
        //   M = perspective(600) * R(axis=(1,1,0), angle=180·t)
        // Hinge: transform-origin at corner-area (0%, 0%) which in our local
        // frame is the TL of the corner-area = (0, -cornerSize). We rotate
        // around the axis (1,1,0) (front-face hypotenuse direction in
        // corner-area-local coords; same direction in our flipped frame).
        float angleDeg = (float)(180.0 * _t);

        // Perspective 600px maps to a SKMatrix44 with M[3,2] = -1/600.
        var perspective = SKMatrix44.CreateIdentity();
        perspective[3, 2] = -1f / 600f;

        // Translate so hinge origin (0, -cornerSize) goes to (0,0,0) for rotation.
        var toOrigin = SKMatrix44.CreateTranslation(0, _cornerSize, 0);
        var fromOrigin = SKMatrix44.CreateTranslation(0, -_cornerSize, 0);

        // Rotate around axis (1, 1, 0). Skia takes axis as Vec3 + angle.
        var axis = new SkiaSharp.SKPoint3(1, 1, 0);
        var rot = SKMatrix44.CreateRotation(axis.X, axis.Y, axis.Z,
            (float)(angleDeg * Math.PI / 180.0));

        // Compose: first translate hinge to origin, then rotate, then untranslate, then perspective.
        // SKMatrix44 multiplication: result = a.Concat(b) means apply b first, then a (column-vector convention).
        var m = SKMatrix44.CreateIdentity();
        m.Concat(perspective);
        m.Concat(fromOrigin);
        m.Concat(rot);
        m.Concat(toOrigin);

        canvas.Concat(in m);

        // Front-face triangle (visible when normal faces viewer — t < 0.5).
        using var path = new SKPath();
        path.MoveTo(0, -_cornerSize);     // TL of corner-area
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
            // Back-face: filled #c9b853. Same triangle path — the rotation has
            // mirrored it across the hinge, so the same path now describes
            // the back face's outline in screen space.
            using var paint = new SKPaint
            {
                Color = new SKColor(0xC9, 0xB8, 0x53, 0xFF),
                IsAntialias = true
            };
            canvas.DrawPath(path, paint);
        }
        // else: front-face with no texture — skip (only happens if Snapshot is null)

        canvas.Restore();
    }
```

- [ ] **Step 2: Update PeelOverlay.Render to pass live t**

Edit `PeelOverlay.cs`. Replace the `Render` method body's `t: 0.0` hardcoded value with the live timeline:

```csharp
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var tex = GetOrDecodeSkBitmap();
        double t = Math.Clamp(_watch.Elapsed.TotalMilliseconds / DurationMs, 0.0, 1.0);
        context.Custom(new PeelDrawOp(bounds, tex, t, cornerSize: CornerSize));
    }
```

- [ ] **Step 3: Temporarily wire for visual smoke test**

Edit `MainWindow.axaml.cs`. In `OnKeyDown`, replace the Ctrl+N branch's `SpawnNew(this); e.Handled = true;` with this temp block:

```csharp
            if (e.Key == Key.N)
            {
                var snap = CaptureCornerSnapshot(this);
                var overlay = new PeelOverlay { Snapshot = snap };
                Grid.SetRow(overlay, 0);
                Grid.SetRowSpan(overlay, 2);
                overlay.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                overlay.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                overlay.Width = PeelOverlay.CornerSize;
                overlay.Height = PeelOverlay.CornerSize;
                overlay.IsHitTestVisible = false;
                BodyGrid.Children.Add(overlay);
                overlay.Start();
                e.Handled = true;
                return;
            }
```

- [ ] **Step 4: Build and visually verify**

Run: `dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe`

Press Ctrl+N. **Expected:** the BL corner triangle rotates over 700ms. For the first half (t < 0.5) you see the textured front-face folding back. For the second half (t > 0.5) you see the dark `#c9b853` back-face. At t=1 (700ms), the back-face is fully visible at its rotated position.

Likely issues to debug if it looks wrong:
- **Triangle stays static / no rotation**: `Concat(in m)` may have wrong API; try `canvas.SetMatrix(canvas.TotalMatrix.Concat(m))` or check SkiaSharp version's exact 4x4 matrix concat method. Some SkiaSharp versions use `canvas.Concat(ref m)` or expose only `Concat(SKMatrix)` (2D). If 4x4 concat isn't available, decompose to per-frame `SKMatrix` (the 2D projection of the 4x4) and apply that.
- **Triangle disappears partway**: backface culling logic flipped. Try inverting `frontVisible` condition.
- **Hinge in wrong place**: adjust `toOrigin` / `fromOrigin` translations — origin should be at corner-area's TL = (0, -cornerSize) in our translated frame.
- **Mirror axis wrong**: rotation axis (1,1,0) should match the front-face's hypotenuse direction (TL→BR diagonal). If the flip looks like it's rotating the wrong way, try axis (1, -1, 0) or (-1, 1, 0).

- [ ] **Step 5: Revert the temp wiring**

Restore the Ctrl+N branch to `SpawnNew(this); e.Handled = true;`. Run `dotnet build -c Debug` — must pass.

- [ ] **Step 6: Commit**

```bash
git add PeelDrawOp.cs PeelOverlay.cs
git commit -m "feat: 3D rotation + back-face triangle (#c9b853) (Stickies-jl1)"
```

---

## Task 7: Add cubic-bezier easing

**Goal:** Apply `cubic-bezier(0.5, 0, 0.4, 1)` easing to t before passing to the draw op.

**Files:**
- Create: `CubicBezier.cs`
- Modify: `PeelOverlay.cs` — apply easing in `Render`

- [ ] **Step 1: Create CubicBezier.cs**

Create `CubicBezier.cs`:

```csharp
using System;

namespace Stickies;

/// <summary>
/// Solves cubic-bezier easing y = f(t) for animations.
/// CSS bezier convention: P0=(0,0), P1=(x1,y1), P2=(x2,y2), P3=(1,1).
/// </summary>
internal sealed class CubicBezier
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
    }

    /// <summary>Maps progress t∈[0,1] to eased output y∈[0,1].</summary>
    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        // Solve x(p) = t for parameter p via Newton-Raphson, then return y(p).
        double p = t;
        for (int i = 0; i < 8; i++)
        {
            double x = X(p);
            double dx = DX(p);
            if (Math.Abs(dx) < 1e-9) break;
            p -= (x - t) / dx;
            p = Math.Clamp(p, 0.0, 1.0);
        }
        return Y(p);
    }

    private double X(double p)
    {
        double oneMinus = 1 - p;
        return 3 * oneMinus * oneMinus * p * _x1
             + 3 * oneMinus * p * p * _x2
             + p * p * p;
    }

    private double DX(double p)
    {
        double oneMinus = 1 - p;
        return 3 * oneMinus * oneMinus * _x1
             + 6 * oneMinus * p * (_x2 - _x1)
             + 3 * p * p * (1 - _x2);
    }

    private double Y(double p)
    {
        double oneMinus = 1 - p;
        return 3 * oneMinus * oneMinus * p * _y1
             + 3 * oneMinus * p * p * _y2
             + p * p * p;
    }
}
```

- [ ] **Step 2: Apply easing in PeelOverlay.Render**

Edit `PeelOverlay.cs`. Add a static field at the top of the class:

```csharp
    private static readonly CubicBezier Easing = new(0.5, 0, 0.4, 1);
```

Modify `Render` to ease t:

```csharp
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var tex = GetOrDecodeSkBitmap();
        double rawT = Math.Clamp(_watch.Elapsed.TotalMilliseconds / DurationMs, 0.0, 1.0);
        double easedT = Easing.Ease(rawT);
        context.Custom(new PeelDrawOp(bounds, tex, easedT, cornerSize: CornerSize));
    }
```

- [ ] **Step 3: Temp-wire and visually verify**

Edit `MainWindow.axaml.cs`. In `OnKeyDown`, replace the Ctrl+N branch's `SpawnNew(this); e.Handled = true;` with this temp block:

```csharp
            if (e.Key == Key.N)
            {
                var snap = CaptureCornerSnapshot(this);
                var overlay = new PeelOverlay { Snapshot = snap };
                Grid.SetRow(overlay, 0);
                Grid.SetRowSpan(overlay, 2);
                overlay.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                overlay.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                overlay.Width = PeelOverlay.CornerSize;
                overlay.Height = PeelOverlay.CornerSize;
                overlay.IsHitTestVisible = false;
                BodyGrid.Children.Add(overlay);
                overlay.Start();
                e.Handled = true;
                return;
            }
```

Run `dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe` and press Ctrl+N. Animation should now feel snappier at the start and slower at the end (bezier(0.5, 0, 0.4, 1) accelerates fast, then decelerates).

- [ ] **Step 4: Revert temp wiring**

Restore the Ctrl+N branch to `SpawnNew(this); e.Handled = true;`. Build to confirm clean.

- [ ] **Step 5: Commit**

```bash
git add CubicBezier.cs PeelOverlay.cs
git commit -m "feat: cubic-bezier(0.5,0,0.4,1) easing on peel timeline (Stickies-jl1)"
```

---

## Task 8: Add radial-gradient drop shadow

**Goal:** Render a radial-gradient shadow under the lifting flap, fading in over `t∈[100/700, 450/700]` (100ms delay + 350ms fade-in within the 700ms total).

**Files:**
- Modify: `PeelDrawOp.cs` — add shadow before drawing the rotated faces; add a separate `_shadowAlpha` parameter

**Note on placement:** The shadow lives in the un-rotated screen frame (it's cast on the page beneath the flap), so it must be drawn BEFORE `canvas.Concat(in m)` is applied — or in a separate `canvas.Save/Restore` block that doesn't include the matrix concat.

- [ ] **Step 1: Add shadow alpha parameter and shadow rendering**

Edit `PeelDrawOp.cs`. Update the constructor signature and field list to accept a separate eased shadow t:

```csharp
internal sealed class PeelDrawOp : ICustomDrawOperation
{
    private readonly Rect _bounds;
    private readonly SKBitmap? _texture;
    private readonly double _t;
    private readonly double _shadowAlpha;   // NEW: 0..1, fades in independently
    private readonly int _cornerSize;

    public PeelDrawOp(Rect bounds, SKBitmap? texture, double t, double shadowAlpha, int cornerSize)
    {
        _bounds = bounds;
        _texture = texture;
        _t = t;
        _shadowAlpha = shadowAlpha;
        _cornerSize = cornerSize;
    }

    // ... rest unchanged ...
}
```

In `Render`, before `canvas.Save()` for the rotation, add a shadow pass:

```csharp
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

        // ... rest of the existing matrix + face rendering code unchanged ...
        // (everything from `float angleDeg = ...` through `canvas.Restore();`)
    }
```

- [ ] **Step 2: Update PeelOverlay.Render to compute shadowAlpha**

Edit `PeelOverlay.cs`. Add a constant for shadow timing at the top of the class:

```csharp
    private const double ShadowDelayMs = 100.0;
    private const double ShadowFadeMs = 350.0;
```

Update `Render` to compute shadowAlpha and pass it through:

```csharp
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var tex = GetOrDecodeSkBitmap();
        double elapsedMs = _watch.Elapsed.TotalMilliseconds;
        double rawT = Math.Clamp(elapsedMs / DurationMs, 0.0, 1.0);
        double easedT = Easing.Ease(rawT);

        double shadowAlpha = Math.Clamp(
            (elapsedMs - ShadowDelayMs) / ShadowFadeMs,
            0.0, 1.0);

        context.Custom(new PeelDrawOp(bounds, tex, easedT, shadowAlpha, cornerSize: CornerSize));
    }
```

- [ ] **Step 3: Temp-wire and visually verify**

Edit `MainWindow.axaml.cs`. In `OnKeyDown`, replace the Ctrl+N branch's `SpawnNew(this); e.Handled = true;` with this temp block:

```csharp
            if (e.Key == Key.N)
            {
                var snap = CaptureCornerSnapshot(this);
                var overlay = new PeelOverlay { Snapshot = snap };
                Grid.SetRow(overlay, 0);
                Grid.SetRowSpan(overlay, 2);
                overlay.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                overlay.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                overlay.Width = PeelOverlay.CornerSize;
                overlay.Height = PeelOverlay.CornerSize;
                overlay.IsHitTestVisible = false;
                BodyGrid.Children.Add(overlay);
                overlay.Start();
                e.Handled = true;
                return;
            }
```

Run `dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe` and press Ctrl+N. **Expected:** as the flap lifts, a soft radial shadow becomes visible underneath the rotating triangle, starting ~100ms in, fully visible by ~450ms. By the time the back-face is fully shown (t=1), the shadow is at full opacity within the triangle clip.

- [ ] **Step 4: Revert temp wiring**

Restore the Ctrl+N branch to `SpawnNew(this); e.Handled = true;`. Build to confirm clean.

- [ ] **Step 5: Commit**

```bash
git add PeelDrawOp.cs PeelOverlay.cs
git commit -m "feat: radial drop shadow under lifting flap (Stickies-jl1)"
```

---

## Task 9: Wire into SpawnNew (final integration)

**Goal:** Replace the existing `SpawnNew` body with the choreography from the spec — snapshot, attach overlay, await Completed, spawn at `near.Position`. Add per-window `_isAnimating` flag. Remove cascade offset. Add null-source fade-in.

**Files:**
- Modify: `MainWindow.axaml.cs`

- [ ] **Step 1: Add per-window flag and fade-in helper**

Edit `MainWindow.axaml.cs`. Add a private field with the other flags near the top of the class:

```csharp
    private bool _isAnimating;
```

Add a private static fade-in helper near the bottom of the class, just before `CaptureCornerSnapshot`:

```csharp
    private static void FadeIn(MainWindow w, int durationMs = 50)
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
```

Add `using System.Diagnostics;` and `using Avalonia.Layout;` at the top of the file if not present.

- [ ] **Step 2: Replace SpawnNew body**

Replace the existing `SpawnNew` method with:

```csharp
    public static void SpawnNew(MainWindow? near)
    {
        // Null-source path: no peel, just fade in at the DB-default position.
        if (near is null)
        {
            var rowN = App.Store.Create(null, null, 280, 280);
            var wN = new MainWindow(rowN);
            FadeIn(wN);
            wN.Show();
            return;
        }

        // Source-driven path: peel from `near`, then spawn B at near.Position
        // (no cascade offset — see spec decision #2).
        if (near._isAnimating) return;
        near._isAnimating = true;

        Bitmap snapshot;
        try
        {
            snapshot = CaptureCornerSnapshot(near);
        }
        catch
        {
            // Snapshot failed (window not yet measured, etc.) — fall back to instant spawn.
            near._isAnimating = false;
            var rowF = App.Store.Create(near.Position.X, near.Position.Y, 280, 280);
            var wF = new MainWindow(rowF);
            wF.Show();
            return;
        }

        var overlay = new PeelOverlay { Snapshot = snapshot };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        overlay.HorizontalAlignment = HorizontalAlignment.Left;
        overlay.VerticalAlignment = VerticalAlignment.Bottom;
        overlay.Width = PeelOverlay.CornerSize;
        overlay.Height = PeelOverlay.CornerSize;
        overlay.IsHitTestVisible = false;

        overlay.Completed += () =>
        {
            // Detach overlay from near's grid (Snapshot/SK bitmap disposed in OnDetached).
            near.BodyGrid.Children.Remove(overlay);
            near._isAnimating = false;

            // Read near.Position FRESH at completion (in case window was dragged
            // during the animation).
            var pos = near.Position;
            var row = App.Store.Create(pos.X, pos.Y, 280, 280);
            var w = new MainWindow(row);
            w.Show();
        };

        near.BodyGrid.Children.Add(overlay);
        overlay.Start();
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build -c Debug`

Expected: PASS, 0 errors. (Possible warning about `Bitmap` ambiguous if both `System.Drawing.Bitmap` and `Avalonia.Media.Imaging.Bitmap` are in scope — fix with explicit `using Avalonia.Media.Imaging;` and remove any System.Drawing using.)

- [ ] **Step 4: Run all 11 manual test cases from the spec**

Build and run: `dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe`

Walk through each test case from `docs/specs/2026-05-06-jl1-peel-animation-design.md` "Manual test cases":

1. **Cold start** — close all notes, kill `Stickies.exe` from Task Manager, relaunch via `bin/Debug/net9.0/Stickies.exe`. No peel anywhere. First note appears (DB-default position) with 50ms fade-in.
2. **Ctrl+N from one note** — peel runs on the note, dark triangle materializes, B appears at A's exact position at end. Drag B aside → A is intact (no residual peeled corner anywhere).
3. **Right-click → New note** — same peel.
4. **Win+Shift+N with at least one note visible** — peel from frontmost.
5. **Win+Shift+N with no notes visible** — close all notes (X each), then trigger hotkey. No peel, just fade-in.
6. **`Stickies.exe --new` from CLI while app running** — open a new shell, run `bin/Debug/net9.0/Stickies.exe --new` — peel from frontmost.
7. **Hold Ctrl+N** — only one peel runs at a time per note.
8. **Ctrl+N, then drag A while peel runs** — overlay follows window. (Tricky to trigger manually — accept "doesn't crash and animation completes" as the bar.)
9. **Ctrl+N, then close A (X) mid-peel** — no B spawns. (Move quickly — close before 700ms elapses.)
10. **Ctrl+N from A, then immediately Ctrl+N from B** — B's peel runs concurrently with any in-flight A peel.
11. **Multi-monitor** — if you have a second monitor with different DPI, drag B to it and Ctrl+N from there → no rendering glitches.

If any test case fails, debug and fix (don't skip). File a follow-up bead only if the failure is genuinely out-of-scope.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.axaml.cs
git commit -m "feat: wire peel into SpawnNew, drop cascade, null-source fade-in (Stickies-jl1)"
```

---

## Task 10: AOT publish verification

**Goal:** Confirm the feature publishes cleanly with AOT and the published binary runs the animation correctly.

- [ ] **Step 1: Publish**

Run (from a Developer Command Prompt, OR with `vswhere.exe` on PATH per CLAUDE.md):

```bash
PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH" \
  dotnet publish -r win-x64 -c Release
```

Expected: completes successfully. The only acceptable warning is the pre-existing `Microsoft.Data.Sqlite` IL2104 (carried from prior work).

If new warnings appear (IL2026/IL2050/IL2070/IL3050) referencing `SkiaSharp` types: those are AOT trim concerns. The contingency from the spec applies — re-evaluate whether to vendor a minimal Skia-free shim. For most usage of `SKMatrix44.CreateRotation` etc. this should not warn, but the lease feature is the main risk surface.

- [ ] **Step 2: Run published binary, verify animation**

Locate the published exe. From the project root:

```bash
ls bin/Release/net9.0/win-x64/publish/Stickies.exe
```

Run it. Test cases 2 and 5 from Task 9 are the minimum — peel works, fade-in works.

- [ ] **Step 3: Measure ship-size delta**

```bash
ls -la bin/Release/net9.0/win-x64/publish/Stickies.exe
ls -la bin/Release/net9.0/win-x64/publish/libSkiaSharp.dll
ls -la bin/Release/net9.0/win-x64/publish/libHarfBuzzSharp.dll
ls -la bin/Release/net9.0/win-x64/publish/e_sqlite3.dll
```

Compare `Stickies.exe` to the prior baseline of 16.6 MB (from `bd memories size-baseline`). Acceptable: ≤+50KB. If +50–500KB, investigate (likely pulled in extra Skia surface area). If >500KB, treat as a regression — investigate before committing further.

- [ ] **Step 4: Commit (no code changes — this task verifies, doesn't change source)**

If Steps 1–3 all pass, no commit needed for this task. Proceed to Task 11.

If a fix was needed (e.g., adding a `[DynamicDependency]` annotation or a trim hint), commit that fix:

```bash
git add <fixed files>
git commit -m "fix: AOT trim hint for Skia managed-API surface (Stickies-jl1)"
```

---

## Task 11: Update size baseline + close bead

**Goal:** Record the new size baseline in `bd remember` (replacing the prior `size-baseline-2026-05-05-after-dropping-angle` key) and close the bead.

- [ ] **Step 1: Record new size baseline**

Replace the existing memory with the new measurements from Task 10 Step 3. Substitute actual numbers measured:

```bash
bd remember --key size-baseline-2026-05-06-after-jl1 \
  "Size baseline 2026-05-06 after peel animation (jl1): \
Stickies.exe <X.X>MB / libSkiaSharp 9.4MB / libHarfBuzzSharp 1.8MB / e_sqlite3.dll 1.7MB = <Y.Y>MB ship total. \
Working set <Z>MB at 4 notes. Stickies.exe grew <delta>KB across jl1 (was 16.6MB at 3f5 close). \
Next high-payoff d0q lever: static-link sqlite via amalgamation (~-1MB net)."
```

Then remove the old key:

```bash
bd forget size-baseline-2026-05-05-after-dropping-angle
```

- [ ] **Step 2: Close the bead**

```bash
bd close Stickies-jl1 --reason="Peel animation shipped. 700ms cubic-bezier, dark back-of-paper triangle, drop shadow. Cascade offset removed (notes stack at source position). Null-source spawns fade in. AOT-clean. See docs/specs/2026-05-06-jl1-peel-animation-design.md and docs/plans/2026-05-06-jl1-peel-animation-plan.md."
```

- [ ] **Step 3: Verify clean state**

```bash
git status
```

Expected: clean working tree, possibly with `.beads/issues.jsonl` updates from `bd close`.

If `.beads/issues.jsonl` is dirty, commit it:

```bash
git add .beads/issues.jsonl
git commit -m "chore: bd export — close Stickies-jl1"
```

---

## Out of Scope (file follow-up beads if these come up)

- Settings toggle to disable animation
- Literal cross-window content reveal (β option in original brainstorm)
- More elaborate fade-in for null-source spawns (e.g., subtle scale-in)
- Animation polish/tuning based on user feel after seeing it ship
- Per-note-color-aware back-of-paper triangle (e.g., source note color × 0.7)
