# Tape blocker + 3 features shipped (locally) — WIP

**Date:** 2026-05-07
**Prior handoff:** [2026-05-07-next-features-locked-ready.md](2026-05-07-next-features-locked-ready.md)
**Status:** `b1p` / `328` / `usm` cherry-picked onto local `main` (3 commits ahead of `origin/main`, not pushed). The V7 padlock from `328` was being replaced with a "tape sticker" visual; that work is uncommitted on disk and currently broken — a transparency-renders-as-black bug appears in the buffer above the header. Paused for follow-up investigation.

## Commits landed this session

| Commit | Bead | What |
|---|---|---|
| `e44c2bd` | [Stickies-b1p](.) | Settings store (versioned `settings.json` + atomic writes + `System.Text.Json` source-gen) |
| `4732671` | [Stickies-328](.) | Per-note Lock in Place — freezes geometry/color, V7 chunky padlock indicator (now superseded on disk) |
| `b8821d6` | [Stickies-usm](.) | Recycle Bin window + auto-purge respecting `Settings.PurgeAfterDays` (0 = never) |

All three closed in bd. Cadence dispatched in parallel via worktrees, cherry-picked with one merge fix in `NoteStore.DeletedColumns`/`ReadDeletedRow` (added `locked` ordinal).

## Uncommitted work

**Modified (tape WIP, broken — see Investigation block below):**

- [src/Views/MainWindow.axaml](../../src/Views/MainWindow.axaml) — `LockIcon` Path replaced with `LockTape` Border (gradient + shadow + RotateTransform). Outer Border wrapped in a Grid so the tape can be a sibling of `BodyBorder`. `BoxShadow` on `BodyBorder` changed from `2 4 16 0 #50000000` → `0 14 14 0 #50000000` (offset = blur, no upward bleed).
- [src/Views/MainWindow.axaml.cs](../../src/Views/MainWindow.axaml.cs) — `private const int LockBuffer = 14`; `MainWindow(Note row)` ctor and `OnSaveBoundsTick` translate stored visible-Y/H ↔ window-Y/H using the buffer based on `_locked`. `OnLockToggleClick` resizes/repositions the window on toggle. `ApplyLocked` toggles `LockTape.IsVisible` and `BodyBorder.Margin.Top` between 0 and 14.

**Untracked (throwaway sketch artifacts):**

- `padlock-variants.html` — 8 padlock SVG variants. User picked V7. Keep until tape work concludes (V1–V8 referenced in iteration history).
- `tape-variants.html` — 8 tape variants. User wanted "V8-like but half-on/half-off the header with slight rotation."

## Tests / build

`dotnet build -c Release` clean (0 warnings, 0 errors). `dotnet publish -c Release -r win-x64 -p:PublishAot=true` clean — only the standing `IL2104` from `Microsoft.Data.Sqlite` (recorded in CLAUDE.md). AOT exe size after `b1p+328+usm`: **17,393,664 bytes** (Δ +552,960 / +540 KB vs `byk` baseline `16,840,704`). Documented but above the per-feature soft targets in the dispatch prompts; under CLAUDE.md hard `<25MB` ceiling.

Local AOT publish requires VS Installer dir on `PATH` first (`vswhere.exe` before `link.exe`). One-liner: `export PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH"`.

## Design invariants (don't re-decide)

1. **Settings.cs uses `System.Text.Json` source-gen with `SettingsJsonContext`.** Reflection-only `JsonSerializer` overloads are AOT-incompatible; `Newtonsoft.Json` is banned per CLAUDE.md. See `src/Services/Settings.cs`.
2. **`PurgeAfterDays = 0` means "never purge"** — startup auto-purge in `App.OnFrameworkInitializationCompleted` skips entirely when value is 0. Doc-comment on `SettingsValues.PurgeAfterDays` is the canonical reference.
3. **`NoteStore.LoadDeleted` uses a separate reader** (`DeletedColumns` / `ReadDeletedRow`) rather than touching the shared `SelectColumns`/`ReadRow`. Active and deleted readers must both surface `locked` (ordinals stay in sync).
4. **`Note` record positional order: `Id, Text, X, Y, Width, Height, Pinned, Color, Locked, DeletedAt = null`.** Default-valued tail goes last; `Locked` is required. Any new property goes after `DeletedAt`.
5. **Lock toggle expands the window upward by `LockBuffer = 14`px and shrinks back on unlock.** Visible note position on screen stays put because `Position.Y -= 14` and `Height += 14` happen together. DB always stores VISIBLE-note geometry; the buffer is in-memory only. `OnSaveBoundsTick` strips/adds the buffer based on `_locked`.
6. **Stickies' single-instance Mutex routes second-instance launches via the named pipe `Stickies.IPC`.** Important for the "running installed MSI vs newly-published exe" footgun: launching `bin/.../publish/Stickies.exe` while an installed `Stickies.exe` is already running just IPCs `SHOW` to the running one — the new build never executes. Always kill any running Stickies before launching a fresh AOT publish.

## Investigation needed: tape transparency black bar

**Symptom:** When a note is locked, the 14px buffer above the visible note (where the tape sticker should peek out) renders as solid/visible black. Both the area inside the tape (lower portion of tape over the buffer) and the flanks (24px strips on either side of the tape) appear black. User: "still showing the black bar above the header" → "the background of the tape" (i.e., black is visible through the tape).

**Tried, didn't help:**

- `BodyBorder.BoxShadow` reduced from `2 4 16 0 #50000000` to `0 14 14 0 #50000000` (offset_y == blur, eliminates upward shadow bleed).
- Tape gradient stops switched from 78% alpha (`#C8E4D2A8` etc.) to fully opaque (`#E4D2A8` etc.).

**Hypothesis space:**

- Avalonia 11.3 `TransparencyLevelHint="Transparent"` may not be honored by this Windows 11 / DWM configuration. The buffer's "transparent" pixels render as the compositor's fallback (black) rather than passing through to the desktop.
- OR the user's desktop / window-behind happens to be dark, and the buffer is correctly transparent — they're seeing legitimate desktop content as "black." Worth confirming by moving the note over a light area and checking.
- OR Avalonia adds an opaque element somewhere (system shadow, decoration) that we haven't spotted.

**Things to try next session:**

- Move the locked note over a deliberately bright area of the desktop. If the "black bar" tracks the desktop content, it's working correctly and the user wants a different design (full-width opaque tape).
- Try `TransparencyLevelHint="Mica"` / `"AcrylicBlur"` and observe.
- Strip the window down to the bare minimum (no shadow, no tape, just the buffer expansion) and see what renders.
- Ask the user whether eliminating the "half off" effect is acceptable (tape entirely within the note's bounds; gives up the skeumorphic feel but is bulletproof).

**Design alternatives if transparency can't be made to render right:**

- **Full-width opaque tape** spanning the buffer. Loses the "small piece of tape" look but eliminates flanks and inner-black entirely.
- **No buffer at all.** Tape sits inside the note's existing bounds, slight rotation suggesting it sticks up. Matches the constraints, loses "half off" aesthetic.
- **Padlock indicator** (V7 from `328`'s commit). User didn't love it; shipped fallback if we abandon tape.

## Start here

```bash
git log --oneline e44c2bd^..HEAD          # see the 3 commits landed
git diff                                  # see tape WIP on disk
ls docs/plans/                            # prior handoff is one above
bd show Stickies-bpy Stickies-4cm         # next-target candidates
```

Tree state: `b1p` / `328` / `usm` on `main` not pushed; `MainWindow.{axaml,axaml.cs}` modified with WIP tape; HTML sketches untracked.

## Sequencing

`(decide tape direction)` → `bpy` → `4cm` (independent, slot in anywhere).

`bpy` (compact context menu) is unblocked now that `b1p` and `328` are closed. It's the visible payoff of the locked cadence — the lock and bin entries will move from the existing vertical menu (where `usm` and `328` parked them as last entries with `// TODO(bpy)` markers) into the new compact actions row.

Tape investigation isn't a `bd` issue — likely 30 minutes of empirical testing once the user is back at the keyboard. Could also be punted entirely (revert WIP, ship V7 padlock or no indicator) before starting `bpy`.

## Out of scope

- **`d0q`** (continuous shrink) — stale `in_progress`. The +540 KB delta from this cadence is documented but not a target. Stay out unless a regression bisects to this trio.
- **`jl1`** (peel animation) — stale `in_progress`. Unrelated to the lock/bin/settings cadence.
- **`edb`** / **`lwc`** — ready P3s but not on the cadence path. `bpy` is the visible payoff first.
- **Pushing to origin.** User wants tape resolved (or punted) before the push. Three commits stay local until then.
