# Peel animation shipped, UAT pending; GitHub remote bootstrapped

**Date:** 2026-05-06
**Prior handoff:** [2026-05-06-feature-burst-ready.md](2026-05-06-feature-burst-ready.md)
**Status:** Peel animation feature complete (10 commits across spec → plan → 8 atomic implementation commits); GitHub remote bootstrapped at `obselate/Stickies`; 11 manual UI test cases pending user verification before bead close.

## Commits landed this session

| Commit | Bead | What |
|---|---|---|
| `2c25ee6` | `Stickies-jl1` (in_progress) | Spec — peel animation design contract. Locked Skia-direct (Option A), stack-at-source-position (Option 1, no cascade), symbolic peel (Option α), per-window animating flag, no settings toggle. |
| `d56ba2c` | `Stickies-jl1` | Implementation plan + bd state. 11 bite-sized tasks, manual-test-driven (no test framework). |
| `9a6c28b` | `Stickies-jl1` | `PeelOverlay` scaffold — `Control` + `Stopwatch`/`DispatcherTimer` lifecycle + `Completed` event + cleanup on detach. No render yet. |
| `6724808` | `Stickies-jl1` | XAML — added `x:Name="BodyGrid"` to inner Grid so runtime overlay attach can use `near.BodyGrid` (no cast). |
| `b4cb6a8` | `Stickies-jl1` | `MainWindow.CaptureCornerSnapshot(near)` — `RenderTargetBitmap` of `BodyBorder` cropped to BL 110×110, anchors crop at BL when note is shorter than 110px. |
| `04818ed` | `Stickies-jl1` | `PeelDrawOp : ICustomDrawOperation` + `PeelOverlay.Render` wired. First Skia render (static front-face triangle from snapshot via `ISkiaSharpApiLeaseFeature.Lease() → SKCanvas → SKShader.CreateBitmap`). **Visually verified on LG monitor with red-tint smoke test.** |
| `eb5bf0b` | `Stickies-jl1` | 3D rotation + back-face. `SKMatrix44` w/ `[3,2] = -1/600` perspective, `PostConcat` composition (NOT `Concat` — doesn't exist), `m.Matrix` 4×4→3×3 reduction (preserves perspective row), `canvas.Concat(ref m2d)`. Backface visibility = `_t < 0.5`. Back-face fill `#c9b853`. |
| `f435640` | `Stickies-jl1` | `CubicBezier` Newton-Raphson solver + `Easing.Ease(rawT)` in `PeelOverlay.Render`. CSS `cubic-bezier(0.5, 0, 0.4, 1)`. |
| `7fd3176` | `Stickies-jl1` | Radial-gradient drop shadow under flap. Drawn BEFORE rotation pass (separate save/restore — shadow lives in screen frame, not flap frame). Clipped to original triangle. Independent timeline `(elapsedMs - 100) / 350` (delay 100ms, fade 350ms, holds at full alpha after). `_shadowAlpha` is now a 4th `PeelDrawOp` ctor param. |
| `0d1654b` | `Stickies-jl1` | Final integration. `SpawnNew(near)` choreography: snapshot → attach overlay → on `Completed` spawn B at `near.Position` (read FRESH at completion). Cascade offset `+24/+24` removed. Per-window `_isAnimating` short-circuits Ctrl+N spam. Null-source path = 50ms `FadeIn` helper, no peel. |
| `93e2d8a` | `Stickies-jl1` | `bd remember size-baseline-2026-05-06-after-jl1` (replaces the dropping-angle key). |
| `0141419` | — | `.gitignore` extended for `.env`, editor, OS files; pre-existing `.beads/.gitignore` already covered `.beads/.env`. |

(Plus an off-the-side action: GitHub remote `origin` added at `https://github.com/obselate/Stickies.git`, local `master` renamed to `main` to match repo default + CLAUDE.md convention, force-pushed over the auto-generated initial commit, repo-local credential helper set to `!gh auth git-credential` for future pushes.)

## Tests / build

- `dotnet build -c Debug` — 0 warnings, 0 errors throughout.
- `dotnet publish -r win-x64 -c Release` — clean apart from the pre-existing `Microsoft.Data.Sqlite` IL2104. **Zero new IL warnings on the SkiaSharp managed surface** (`SKMatrix44`, `SKShader.CreateBitmap`, `SKShader.CreateRadialGradient`, `SKBitmap.Decode`, `SKCanvas.Concat(ref SKMatrix)`).
- Published binary smoke test passed (process stayed up 2s, terminated cleanly).
- Static front-face Skia render visually confirmed during Task 5 (red-tint smoke triangle in BL of note matched expected geometry).
- Rotation/back-face/easing/shadow/SpawnNew choreography NOT yet visually verified (Task 9's 11 manual cases).

## Design invariants (don't re-decide)

1. **`SKMatrix44` composition uses `PostConcat`, not `Concat`.** `Concat` does not exist on `SKMatrix44` in SkiaSharp 2.88.x. `PostConcat(b)` = `this = this × b`. Building right-multiply order `M = perspective × fromOrigin × rot × toOrigin` from identity is achieved by calling `PostConcat(perspective)`, `PostConcat(fromOrigin)`, `PostConcat(rot)`, `PostConcat(toOrigin)` in that sequence. See `PeelDrawOp.cs`.
2. **Apply `SKMatrix44` to `SKCanvas` via `m.Matrix` 3×3 reduction.** No `SKCanvas.Concat(SKMatrix44)` overload exists in 2.88. The `SKMatrix44.Matrix` property drops the Z column/row but preserves the perspective row, yielding a 3×3 `SKMatrix` of form `[ScaleX SkewX TransX / SkewY ScaleY TransY / Persp0 Persp1 Persp2]` — sufficient for rendering a 2D triangle under a 3D-with-perspective transform. Apply via `canvas.Concat(ref m2d)`.
3. **Coordinate system inside `PeelDrawOp.Render`.** After `canvas.Translate(_bounds.X, _bounds.Y + _cornerSize)`, origin (0,0) = BL of overlay region; x→right, y→down (so going UP from origin uses negative y). Corner-area box occupies `x∈[0, cs]`, `y∈[-cs, 0]`. Front-face triangle: `(0,-cs)` TL, `(cs,0)` BR, `(0,0)` BL (right angle).
4. **Shadow is drawn BEFORE the rotated face pass, in its own save/restore.** The shadow lives in screen frame (cast on the page beneath), not in the flap's rotated frame. If you put the shadow inside the matrix-concat block, it rotates with the flap — wrong.
5. **Snapshot is converted to `SKBitmap` via PNG round-trip.** `Bitmap.Save(MemoryStream)` → `SKBitmap.Decode(stream)`. Slow but only happens ONCE per animation (cached in `_skSnapshot`, disposed in `OnDetachedFromVisualTree`). ~1ms for 110×110, fine.
6. **Cascade offset is gone (Stickies-jl1 spec decision #2).** `SpawnNew(near)` no longer adds `+24/+24` to position. New notes stack at the source's exact `Position`. User drags them apart manually. **Do not reintroduce cascade without re-opening the spec.**
7. **`near.Position` is read FRESH inside the `Completed` callback**, not captured at trigger time. So if the user drags `near` during the 700ms animation, B spawns where `near` is at end-of-animation, not where it was at start.
8. **`_isAnimating` is per-window**, not global. A peel running on note A does NOT block a concurrent peel on note B. Spamming Ctrl+N on the SAME note is the only thing the flag suppresses.
9. **Null-source path uses `FadeIn` helper, not the overlay.** Hotkey with no frontmost / IPC `--new` at startup with no notes existed → simple 50ms `Opacity` 0→1 ramp on the new window. No Skia path involved. Don't try to peel from "nothing".
10. **GitHub remote credential helper is repo-local.** `git config credential.helper '!gh auth git-credential'` was set at the repo level (not global) so other repos on this machine still use whatever they were using (likely Git Credential Manager). If the persistent helper ever breaks, fall back to inline `git -c credential.helper= -c credential.helper='!gh auth git-credential' push`.

## Start here

```bash
git log --oneline -12          # 11 commits + the chore: gitignore land at the top
bd show Stickies-jl1           # what's pending: 11 manual UI tests in the design spec
dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe
# → walk the 11 cases at docs/specs/2026-05-06-jl1-peel-animation-design.md "Manual test cases"
# If all 11 pass: bd close Stickies-jl1 --reason="Peel shipped; UAT verified on $(date)"
```

## Sequencing

`Stickies-jl1` UAT (close it) → `Stickies-94r` (MSI installer, last v1 shipping concern) → `Stickies-edb` (image paste — concrete user value, lights up Skia further) → `Stickies-drm` (inline markdown) → `Stickies-har` (better color picker, supersedes the dated `ChooseColor` modal).

**Why:** jl1 is technically code-complete but unverified — closing it cleanly is a 5-minute UAT pass. After that, `94r` is the only remaining v1 shipping concern; everything else is enhancement. The current ship size is 28.27 MiB total / `Stickies.exe` 15.89 MiB (16,665,088 bytes); next d0q lever (static-link sqlite via amalgamation, ~−1MB) hasn't been touched.

## New this session

- **Subagent-driven development** (`superpowers:subagent-driven-development`) with Sonnet 4.6 worked well for the 11-task plan. Pattern: I dispatch implementer with full task text + scene context, subagent does code+build+commit, I do visual smoke tests at integration milestones (Task 5 was the only one I successfully verified — Task 6+ couldn't be screenshotted because the user was AFK and Stickies was on a virtual desktop the screenshot tool couldn't reach).
- **Subagent caught a real API bug.** Task 6's spec called `m.Concat(...)` and `canvas.Concat(in SKMatrix44)`; subagent verified neither exists in SkiaSharp 2.88 and reported BLOCKED rather than guessing — exactly the right behavior. Authorized fix was `PostConcat` + `m.Matrix` extraction (now invariant 1 & 2).
- **Stickies single-instance lock prevents fresh launches while it's running.** When debugging via `bin/Debug/.../Stickies.exe &`, `taskkill //F //IM Stickies.exe` first or the new launch dispatches `SHOW` to the existing process.
- **Computer-use across virtual desktops.** The `mcp__computer-use__screenshot` tool sees only the active virtual desktop on the chosen monitor. If a window is on a different vdesk, screenshots come back without it. No clean programmatic way to switch vdesks from the tool — ask the user, or skip visual verification and rely on subsequent integration steps to surface bugs.
- **`gh auth git-credential` as the repo-local credential helper** is the cleanest way to push to GitHub when `gh` is logged in but Git Credential Manager wants to pop a UI dialog (which a headless agent can't dismiss). Set persistently via `git config credential.helper '!gh auth git-credential'`.

## Key artifacts

- Spec: [docs/specs/2026-05-06-jl1-peel-animation-design.md](../specs/2026-05-06-jl1-peel-animation-design.md)
- Plan: [docs/plans/2026-05-06-jl1-peel-animation-plan.md](2026-05-06-jl1-peel-animation-plan.md)
- Bead notes: `bd show Stickies-jl1` (locked geometry, end-state, iteration log)
- Mockup demos: `.superpowers/brainstorm/632696-1778039163/` (gitignored, local-only — peel-v3 stage-F, peel-orient variant 2)

## Out of scope

- **`Stickies-d0q` static-link sqlite (next lever).** ~−1MB net but invasive MSBuild work (vendoring sqlite3.c, `cl.exe` invocation in `LinkNative`). No reason to start it without a clear shipping push.
- **`Stickies-4cm` FTS5 search / `Stickies-usm` recycle bin.** Both gated on a manager/search window UX that hasn't been brainstormed yet — don't pick up cold.
- **Settings toggle to disable peel animation.** Spec decision #4 was YAGNI on this. Revisit only if user feedback demands; will need a settings UI, which is its own bead.
- **Literal cross-window peel reveal (β option).** Spec decision #3 went symbolic (α). Do not re-litigate without spec change.
- **`bd dolt push` setup.** No bd remote configured; `bd dolt push` reports "skipping" cleanly. Not blocking — the canonical record is `.beads/issues.jsonl` in git.
