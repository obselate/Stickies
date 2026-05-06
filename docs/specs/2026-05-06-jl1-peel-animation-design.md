# Spec — 2.5D peel animation on note spawn (Stickies-jl1)

**Date:** 2026-05-06
**Bead:** Stickies-jl1
**Status:** Design approved, awaiting implementation plan

## Summary

When the user spawns a new note from an existing note (Ctrl+N, right-click → New note, Win+Shift+N with notes visible, or `--new` IPC verb with a frontmost note), play a 700ms 2.5D peel animation on the source note's bottom-left corner. The peel's dark back-of-paper triangle (`#c9b853`) is the visual indicator that a new note has been torn off. At t=700ms, the new note's window appears at the source note's exact `Position` (no cascade offset). Notes stack at the same position; user separates them by dragging.

When there is no source note (hotkey with no visible notes, IPC `NEW` at startup with no notes), no peel runs — new note fades in (`Opacity` 0→1, 50ms).

## Decisions locked

| # | Decision | Choice |
|---|---|---|
| 1 | Implementation approach | **A — raw Skia via `ISkiaSharpApiLeaseFeature`**, custom Avalonia `Control` overlay. (Bead options A vs B; B's "slide+tilt+fade" is a different effect, not a peel.) |
| 2 | Where the new note lands | **Stack at source's exact position.** Cascade offset removed. (Bead Option 1 of 3.) |
| 3 | Reveal mechanic during peel | **Symbolic (Option α).** Peel renders inside source's window only; new note pops into existence at t=700ms. No literal cross-window reveal of new note's content through the peel hole. (The new note is brand new — there is no "underneath" content to reveal anyway.) |
| 4 | Settings toggle to disable | **None.** YAGNI; no settings infrastructure exists. Revisit if user feedback demands. |
| 5 | Source-to-source choreography | **Per-window flag.** A peel running on note A does not block a peel from note B. Repeated Ctrl+N on the same note while its peel is running is ignored. |
| 6 | Source closed/deleted during peel | **Abort.** No new note is spawned. |

## Locked visual (carried from bead notes — DO NOT re-decide)

- **`corner-area`**: 110×110 box anchored at the source note's bottom-left.
- **Front-face clip**: `polygon(0 0, 100% 100%, 0 100%)` — visible BL tip triangle, right angle at BL.
- **Back-face**: same shape, mirrored via `rotateY(180deg)`-equivalent matrix.
- **Hinge**: `transform-origin = (0%, 0%)`, axis = `(1, 1, 0)` — runs along the front-face's hypotenuse (TL→BR diagonal of corner-area).
- **Animation**: `rotate3d(1, 1, 0, 0deg → 180deg)` over **700ms**, easing `cubic-bezier(0.5, 0, 0.4, 1)`.
- **Drop shadow**: radial gradient under the lifting flap, fades in over 350ms with 100ms delay.
- **Back-of-paper color**: `#c9b853` (uniform, ignores per-note color for now — can revisit if it clashes with non-yellow notes).
- **Perspective**: 600px, perspective-origin: `30% 70%`.

End state at t=700ms: source note appears unchanged (peel collapses; overlay disposed). New note window is on screen at source's `Position`.

## Architecture

```
┌─ MainWindow A (existing note, source of spawn) ─────────────┐
│  ┌─────────────────────────────────────────────────────────┐│
│  │ HeaderBar                                               ││
│  ├─────────────────────────────────────────────────────────┤│
│  │ TextBox / body                                          ││
│  │                                                         ││
│  │ ┌──────────────┐                                        ││
│  │ │ PeelOverlay  │ ← Custom Control, 110×110, anchored    ││
│  │ │   ◢          │   bottom-left of body. Renders peel    ││
│  │ │              │   via ISkiaSharpApiLeaseFeature.       ││
│  │ │              │   IsHitTestVisible=False.              ││
│  │ └──────────────┘                                        ││
│  └─────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────┘
```

### Components

**1. `PeelOverlay : Control`** *(new file)*

- Custom Avalonia `Control` rendering raw Skia.
- Properties:
  - `SnapshotBitmap` — pre-animation pixels of the BL corner-area, used as the front-face texture
  - `Duration` = 700ms (constant for now)
  - `BackColor` = `#c9b853` (constant)
- State:
  - `Stopwatch` driving 0→700ms timeline
  - `DispatcherTimer` ~16ms tick → `InvalidateVisual()` per frame
- `Render(DrawingContext ctx)`:
  1. Lease `ISkiaSharpApiLeaseFeature` from `ctx`. If unavailable (non-Skia backend), skip render.
  2. Compute `t = Math.Clamp(Stopwatch.Elapsed.TotalMilliseconds / 700.0, 0.0, 1.0)`.
  3. Apply easing `cubic-bezier(0.5, 0, 0.4, 1)` to get eased `t'`.
  4. Build `SKMatrix44`: perspective(600) × translate(hinge origin) × rotateAxisAngle(axis=(1,1,0), 180·t' degrees) × translate(−hinge origin).
  5. Draw front-face triangle: texture-mapped from `SnapshotBitmap`, clipped to `polygon(0 0, 100% 100%, 0 100%)`.
  6. Draw back-face triangle: filled `#c9b853`, same clip.
  7. Draw radial-gradient shadow under flap (alpha ramps 0→max over `t ∈ [0.143, 0.643]` — i.e., 100ms delay + 350ms fade-in within 700ms total).
- Events:
  - `Completed` fires once when `t` reaches 1.0
- `OnDetachedFromVisualTree`: stop timer, suppress `Completed`.

**2. `MainWindow.SpawnNew(MainWindow? near)`** *(modified)*

Current code (`MainWindow.axaml.cs:208-219`):

```csharp
public static void SpawnNew(MainWindow? near)
{
    int? x = null, y = null;
    if (near is not null)
    {
        x = near.Position.X + 24;   // ← cascade offset to remove
        y = near.Position.Y + 24;
    }
    var row = App.Store.Create(x, y, 280, 280);
    var w = new MainWindow(row);
    w.Show();
}
```

New shape:

```
if (near is null) {
    // Existing null-source path unchanged: App.Store.Create(null, null, 280, 280)
    // lets the DB layer pick the default position.
    var row = App.Store.Create(null, null, 280, 280);
    var w = new MainWindow(row);
    w.Opacity = 0; w.Show();
    fade w from 0→1 over 50ms via DispatcherTimer or simple SetterAnimation;
    return;
}

if (near._isAnimating) return;  // ignore spammed Ctrl+N on same source

near._isAnimating = true;
snapshot = capture BL 110×110 region of near's body via RenderTargetBitmap;

overlay = new PeelOverlay { SnapshotBitmap = snapshot };
overlay.Completed += () => {
    detach overlay; dispose snapshot;
    // Cascade offset removed: B spawns at near's exact Position.
    var row = App.Store.Create(near.Position.X, near.Position.Y, 280, 280);
    var w = new MainWindow(row);
    w.Show();
    near._isAnimating = false;
};

attach overlay to near's BodyBorder's inner Grid as last child (top of Z stack);
overlay.Start();
```

**3. `MainWindow.axaml`** *(no structural change required)*

The existing layout is `BodyBorder > Grid (rows: 22, *)`. PeelOverlay is inserted at runtime as a child of that inner Grid:

```csharp
Grid.SetRow(overlay, 0);
Grid.SetRowSpan(overlay, 2);
overlay.HorizontalAlignment = HorizontalAlignment.Left;
overlay.VerticalAlignment = VerticalAlignment.Bottom;
overlay.Width = 110;
overlay.Height = 110;
overlay.IsHitTestVisible = false;
((Grid)BodyBorder.Child).Children.Add(overlay);
```

`RowSpan=2` over both rows is defensive — the overlay's `VerticalAlignment=Bottom` keeps it pinned to the body's BL regardless. Naming `BodyBorder.Child` as a known `Grid` may need a small XAML tweak (`x:Name` on the Grid) so we can address it without a `(Grid)` cast; either approach is fine.

**4. `Stickies.csproj`** *(possibly modified)*

- Verify `SkiaSharp` is reachable transitively through `Avalonia`. If `ISkiaSharpApiLeaseFeature` and `SKMatrix44` aren't accessible without an explicit reference, add `<PackageReference Include="SkiaSharp" Version="..." />` matching the version Avalonia 11.3 uses.

### Data flow

```
Ctrl+N (or right-click "New note")
  → MainWindow.OnNewClick → SpawnNew(this)

Win+Shift+N
  → HotkeyHost WndProc → SpawnNew(null)
    OR (if frontmost note exists) FrontmostNote → SpawnNew(frontmost)

Stickies.exe --new (IPC)
  → App.HandleVerb("NEW") → SpawnNew(FrontmostNote(desktop))

SpawnNew(near):
  near != null → snapshot → PeelOverlay → on Completed → spawn B at near.Position
  near == null → spawn B at fallback → fade-in
```

No DB changes. No schema migration.

### Window choreography

- A is unchanged throughout the animation (overlay sits on top, A's underlying content is not transformed).
- B's window is created and shown at t=700ms.
- B is naturally Z-top (just-created window).
- A and B end up at the same `Position`. User drags them apart manually.
- Cascade offset (`x = near.Position.X + 24, y = near.Position.Y + 24`) is removed from `SpawnNew`.

### AOT considerations

- `ISkiaSharpApiLeaseFeature`, `SKMatrix44`, `SKCanvas.DrawBitmap` etc. must be exercised under `dotnet publish -r win-x64 -c Release` with no new IL2026/IL2104/IL3050 trim warnings.
- Avalonia's Skia surface is already AOT-clean (we ship libSkiaSharp.dll). Direct managed-API access on top of that surface is the unverified-by-this-project bit.
- If a SkiaSharp managed type is trim-stripped and causes runtime failure post-publish, fall back to vendoring a minimal `SKMatrix44`/`Render` shim that uses only AOT-safe Avalonia primitives. (Backup plan; not expected to be needed.)

## Edge cases & behaviors

| Situation | Behavior |
|---|---|
| `near` is null (hotkey w/ no frontmost, IPC startup `--new`, no notes existed) | Skip peel. New window fades in (`Opacity` 0→1 over 50ms). |
| Ctrl+N pressed again on same `near` while its peel still running | Ignore second press. `near._isAnimating == true` short-circuits at top of `SpawnNew`. |
| Ctrl+N from a different note (B) while A's peel is running | B's peel runs concurrently — flag is per-window. |
| `near` is being window-dragged when user hits Ctrl+N | Animation runs anyway. Overlay is parented to `near`'s body Grid, follows window. New note B spawns at `near.Position` **at the moment `Completed` fires** (read fresh), not at trigger time. |
| `near` window closed (X) during animation | `PeelOverlay.OnDetachedFromVisualTree` cancels timer, suppresses `Completed`. New B does NOT spawn. |
| `near` deleted (Ctrl+D) during animation | Same as window closed. |
| New B spawn fails (DB error) | Animation completes; no B appears. Swallow silently for now (no logging infrastructure). File follow-up bead if it bites. |
| Slow CPU, software renderer can't hit 60fps | Acceptable. `Stopwatch.Elapsed / 700.0` clamps t∈[0,1] regardless of frame cadence; animation completes in real time even if frames drop. |
| Multi-monitor / DPI scaling | Snapshot captured in DIPs via Avalonia's `RenderTargetBitmap`. PeelOverlay sized in DIPs. Skia handles per-monitor DPI. |

## Performance budget

- Animation only runs on Ctrl+N from an existing note. **Does not touch cold start.**
- Per-frame work: 110×110 perspective-mapped quad + filled triangle + radial shadow gradient. CPU Skia <1ms/frame on modern hardware.
- Memory: one 110×110 BGRA bitmap per active animation = ~50KB. Disposed at `Completed`.
- Total animation duration: 700ms fixed.

## Acceptance criteria

- [ ] Peel renders matching the locked spec (front rotates → `#c9b853` back-of-paper triangle visible at end of 700ms).
- [ ] B appears at A's exact `Position` at t=700ms. No cascade offset anywhere.
- [ ] All 11 manual test cases (below) pass.
- [ ] `dotnet publish -r win-x64 -c Release` clean (no new trim warnings beyond existing `Microsoft.Data.Sqlite` IL2104).
- [ ] Ship size impact recorded; `bd remember` `size-baseline-*` key updated.
- [ ] No subjective regression to cold-start time.

## Manual test cases

1. Cold start → no peel anywhere, no perf regression on first paint.
2. Open one note, Ctrl+N → peel on A's BL, dark triangle appears, B materializes at A's position. Drag B aside → A intact (no residual peeled corner).
3. Right-click → "New note" → same peel (uses `SpawnNew(this)` path).
4. Win+Shift+N with at least one note visible → peel from frontmost.
5. Win+Shift+N with no notes visible → no peel, fade-in only.
6. `Stickies.exe --new` from CLI while app running → peel from frontmost.
7. Hold Ctrl+N → only one peel runs at a time per note.
8. Ctrl+N, then drag A while peel runs → overlay follows window.
9. Ctrl+N, then close A (X) mid-peel → no B spawns.
10. Ctrl+N from A, then immediately Ctrl+N from B → both peels run concurrently.
11. Multi-monitor: spawn on monitor 1, drag B to monitor 2 with different DPI → no rendering glitches on subsequent peels from B.

## Files touched

- `PeelOverlay.cs` *(NEW)* — custom Avalonia Control with raw Skia render.
- `MainWindow.axaml` — designate `BodyBorder`'s child Grid (or convert child to Grid) as overlay host.
- `MainWindow.axaml.cs` — modify `SpawnNew` (snapshot, attach overlay, await Completed, remove cascade offset); add per-window `_isAnimating` flag; add fade-in fallback for null-source spawn.
- `Stickies.csproj` — add explicit `SkiaSharp` PackageReference if needed.

## Out of scope (file follow-up beads if needed)

- Settings toggle to disable animation.
- Literal cross-window content reveal (β option in original brainstorm).
- More elaborate fade-in for null-source spawns.
- Animation polish/tuning based on user feel after seeing it ship.
- Per-note-color-aware back-of-paper triangle (e.g., source note color × 0.7) — currently uniform `#c9b853`.

## References

- Bead: `bd show Stickies-jl1`
- Mockup demos: `.superpowers/brainstorm/632696-1778039163/` (peel-v3 stage-F, peel-orient variant 2)
- Locked geometry: bead notes section "TARGET END-STATE" and "CONFIRMED MECHANICS"
- Project conventions: `CLAUDE.md`
- Prior handoff: `docs/plans/2026-05-06-feature-burst-ready.md`
