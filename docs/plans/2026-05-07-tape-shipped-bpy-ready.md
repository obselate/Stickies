# Tape host shipped, bpy ready

**Date:** 2026-05-07
**Prior handoff:** [2026-05-07-tape-blocker-and-features-shipped-wip.md](2026-05-07-tape-blocker-and-features-shipped-wip.md)
**Status:** Tape host (`jc7`) shipped and pushed. Four-feature cadence (`b1p` / `328` / `usm` / `jc7`) is complete on `origin/main`. Next bead is `bpy` — the compact context menu, which lands the visible payoff of the cadence by consolidating the lock / bin / settings entries that the trio parked at the end of the existing vertical menu.

## Commits landed this session

| Commit | Bead | What |
|---|---|---|
| [`3b9f7f9`](.) | [Stickies-jc7](.) | Cross-platform tape host: `WS_EX_LAYERED` + Skia ARGB on Win32, owned Avalonia child window on Mac/Linux |

(The four prior handoff commits — `b1p`, `328`, `usm`, and the handoff doc itself — were committed before this session but pushed during it.)

## Tests / build

`dotnet build` clean (0 warn, 0 err). AOT publish clean (only the standing `IL2104` from `Microsoft.Data.Sqlite`). `Stickies.exe` = 17,403,392 bytes — Δ +9,728 / +9.5 KB vs the `b1p+328+usm` baseline. Hard ceiling 25 MB → 8.4 MB headroom.

## Design invariants (don't re-decide)

1. **Win32 stays on `Win32RenderingMode.Software`.** AngleEgl was tested this session — it works, but ships +5.4 MB `av_libglesv2.dll` and pushes idle RAM from ~44 MB to ~115 MB. Both are above CLAUDE.md's permanent-priority size/RAM ceilings. Wgl was also tested as a middle path, but it composes via `RedirectionSurface` like Software does (`WinUIComposition`/`DirectComposition` need D3D-backed textures, which only ANGLE produces). See [src/Program.cs:67-78](../../src/Program.cs) and `bd memories tape` for the full chain.
2. **Per-pixel-alpha visuals on Win32 use `ITapeHost`-style escape hatches**, not Avalonia transparent windows. Layered window + Skia-rendered ARGB bitmap is the established pattern (see [src/Win32/WinTapeHost.cs](../../src/Win32/WinTapeHost.cs)). Avalonia's `Background="Transparent"` and `Background="{x:Null}"` both compose against an opaque-black-cleared `RedirectionSurface` under Software rendering — neither produces real transparency. `ActualTransparencyLevel = Transparent` reports honestly that the platform layer accepted the hint, but it lies about what the pipeline actually does to alpha-0 pixels.
3. **DPI conversion lives at the boundary.** Avalonia's `Window.Position` is `PixelPoint` (device pixels) but `Window.Width`/`Height` are DIPs. Mixing at the call site is the bug pattern that broke tape sizing on 4K. `ITapeHost.Update` takes `(PixelRect deviceBounds, double scale)`; each impl converts internally. `Pad` scales additionally with tape width because rotation extends the AABB by `sin(angle) × width`.
4. **Locked notes don't move.** `OnDragBarPressed` and `OnResizeGripPressed` early-return on `_locked`. The tape's `Update`-on-`PositionChanged`/`SizeChanged` wiring is therefore a no-op in steady state, but it's harmless and lets future programmatic-reposition flows just work.
5. **Mac/Linux tape works because of GPU defaults.** Both platforms ride Avalonia defaults (Metal/OpenGl, Egl) — these support per-pixel alpha out of the box. `AvaloniaTapeHost` is therefore an Avalonia child-window impl shared by both; no ObjC / Xlib P/Invoke needed unless those defaults regress.

## Start here

```bash
git log --oneline -3            # confirm 3b9f7f9 is HEAD on main
bd show Stickies-bpy            # the compact context menu spec
grep -rn "TODO(bpy)" src        # 3 markers — bin entry + 2 SettingsWindow wire-up sites
```

## Sequencing

`bpy` is the only ready P2 after the cadence. It's intentionally next: it consolidates the lock entry (from `328`), bin entry (from `usm`), and a future settings entry (from `b1p`) into a compact actions row, making the cadence's features discoverable. `4cm` / `edb` / `lwc` are P3s and not on the cadence path.

## New this session

- **The `Background="Transparent"` vs `Background="{x:Null}"` distinction is moot under Win32 Software rendering** — both render uncovered/alpha-0 pixels as black because the swap-chain is opaque-cleared. The Avalonia docs example pairing `{x:Null}` with `TransparencyLevelHint=Transparent` only works once a GPU composition mode is in play.
- **`bd`'s auto-export `git add` warning fires on every `create` / `update` / `close`** in this repo. Benign — the `.beads/` data is in Dolt regardless. Ignore.

## Key artifacts

- Spec: `bd show Stickies-bpy`
- Tape implementation: [src/Win32/WinTapeHost.cs](../../src/Win32/WinTapeHost.cs), [src/Platform/AvaloniaTapeHost.cs](../../src/Platform/AvaloniaTapeHost.cs), [src/Platform/ITapeHost.cs](../../src/Platform/ITapeHost.cs), [src/Platform/TapeHost.cs](../../src/Platform/TapeHost.cs)
- Tape wiring in MainWindow: [src/Views/MainWindow.axaml.cs](../../src/Views/MainWindow.axaml.cs) — `_tapeHost` field, `UpdateTape()`, `ApplyLocked()`, `OnWindowOpened`, `Closing` lambda

## Out of scope

- **`d0q`** (continuous shrink) — stale `in_progress`. The +562 KB delta across the cadence is documented but not a regression target.
- **`jl1`** (peel animation) — stale `in_progress`. Unrelated to `bpy`.
- **Mac/Linux tape visual verification** — handled when next at those machines; `AvaloniaTapeHost` builds clean on the three-OS CI matrix and the GPU-defaults assumption is well-grounded, but no human has yet confirmed the tape renders with desktop showing through on Mac or Linux.
- **`4cm` / `edb` / `lwc`** — P3s, deferred until `bpy` lands.
