# Feature burst — five core features shipped, ready for next pick

**Date:** 2026-05-06
**Prior handoff:** [2026-05-05-ready-beads-ready.md](2026-05-05-ready-beads-ready.md)
**Status:** Six commits landed. Single-instance, right-click menu (default-on-top), global hotkey, pin toggle, per-note color, and ANGLE-strip all shipped and visually verified. 8 ready beads (4 newly filed during this session). Tree clean apart from a dirty `.beads/issues.jsonl` from late `bd update` calls — that gets committed with this handoff.

## Commits landed this session

| Commit | Bead | What |
|---|---|---|
| `796bb12` | `Stickies-y12` (closed) | Single-instance: `Local\Stickies.SingleInstance` mutex + `Stickies.IPC` named pipe. Verbs `SHOW` (default; activates open windows or restores from DB if all were closed via X), `NEW` (--new flag, used by global hotkey), `OPEN:<id>` (reserved for future manager). `ShutdownMode = OnExplicitShutdown`. |
| `1f7521f` | `Stickies-rt1` (closed) | Right-click `ContextMenu` on the note's root Border. `New note` / `Delete` items mirror Ctrl+N / Ctrl+D via shared `DeleteNote()` helper. Folded in default `Topmost="True"` per user direction. |
| `d609041` | `Stickies-8f3` (closed) | Global hotkey **Win+Shift+N**. Hidden 1×1 message-pump `HotkeyHost` Window registers via `RegisterHotKey`, intercepts `WM_HOTKEY` (0x0312) through `Avalonia.Controls.Win32Properties.AddWndProcHookCallback`, posts `MainWindow.SpawnNew(null)`. AOT-clean (`[LibraryImport]`). Required `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for `LibraryImport`'s source-gen marshalling. |
| `0ea3b21` | `Stickies-b06` (closed) | Per-note pin toggle. Schema: `pinned INTEGER NOT NULL DEFAULT 1` — existing notes pin on migration, matching rt1's baseline. Menu item between New and Delete swaps text "Unpin from top" ↔ "Pin on top". `XAML Topmost="True"` removed; now set in ctor from `row.Pinned`. |
| `6076720` | `Stickies-265` (closed) | Per-note color. Schema: `color TEXT NOT NULL DEFAULT '#FFF59E'`. Inline row of 5 preset swatches + a 6th `…` swatch that opens the native Win32 `ChooseColor` dialog (`comdlg32` via `[LibraryImport]`). Header strip auto-derives via uniform RGB × 0.92. Current swatch outlined in RoyalBlue. `BodyBorder` and `HeaderBar` named for runtime swap. |
| `3f6ef5d` | `Stickies-d0q` (in_progress, continuous) | Lever 2 — dropped ANGLE/GLES backend. `Win32PlatformOptions.RenderingMode = [Software]` + `StripUnusedNatives` MSBuild target filters `av_libglesv2.dll` from `ResolvedFileToPublish`. **−5.4 MB ship size**. Software renderer is plenty for static text on flat color. |

## Uncommitted work

- `.beads/issues.jsonl` — exported state from `bd update Stickies-jl1 --notes=…` (peel animation design confirmed mid-conversation) and `bd close` calls after the last code commit. Commit with this handoff.

## Tests / build

- `dotnet build -c Debug` — clean (0 warnings, 0 errors).
- `dotnet publish -r win-x64 -c Release` — clean apart from one carried-over `IL2104` warning from `Microsoft.Data.Sqlite` (untouched all session).
- Published exe verified runtime-OK with the ANGLE DLL absent. Working set ≈ 54 MB at 4 notes (under 60 MB budget).

## Sizes (current baseline — also captured via `bd remember`)

| Artifact | Size |
|---|---|
| `Stickies.exe` | 16.6 MB (was 15.5 MB at xc4 — gained 1.1 MB for 5 features) |
| `libSkiaSharp.dll` | 9.4 MB |
| `libHarfBuzzSharp.dll` | 1.8 MB |
| `e_sqlite3.dll` | 1.7 MB |
| **Ship total** | **29.6 MB** (down from 35.0 MB pre-strip) |

## Design invariants (don't re-decide)

1. **IPC architecture is final.** Mutex name `Local\Stickies.SingleInstance`, pipe name `Stickies.IPC`, verbs `SHOW` / `NEW` / `OPEN:<id>`. Per-session scope (auto-released on crash). `SHOW` recovers windows from DB when all were closed via X. `[Program.cs](../../Program.cs)`, [App.axaml.cs](../../App.axaml.cs).
2. **`ShutdownMode = OnExplicitShutdown` is intentional.** Process survives closing the last note. Without a manager UI the only way back is a second launch's `SHOW` verb. Tradeoff documented in `Stickies-rt1` / `Stickies-usm`.
3. **Pin state is read from the DB, not hardcoded in XAML.** `MainWindow.axaml` MUST NOT have `Topmost="True"` — `MainWindow.axaml.cs` `ApplyPinned(row.Pinned)` sets it. Schema default is `1` so new notes pin by default.
4. **Software renderer is mandatory.** `Win32PlatformOptions.RenderingMode = [Software]` + the `StripUnusedNatives` target in `Stickies.csproj` together drop ANGLE. Reverting either re-introduces the 5.4 MB. If a future feature needs GPU (animations, shaders), do the math first against d0q.
5. **`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` is required by `[LibraryImport]`.** Source-gen marshalling stubs use `unsafe` under the hood. We use no actual `unsafe` blocks in our source — don't introduce any.
6. **Header strip color is auto-derived (RGB × 0.92).** Single source of truth = the chosen body color. `Darker()` helper in `MainWindow.axaml.cs`. Don't add a separate `header_color` column.
7. **`ChooseColor` UX is acknowledged-dated, not a bug.** Tracked in `Stickies-har`; leading replacement candidate is option 2 (expanded preset palette inside the menu, no modal dialog at all).
8. **Closing a note window does NOT delete the note** (carried from prior handoff — still load-bearing). Soft-delete is Ctrl+D only.
9. **AOT publish needs `vswhere.exe` on `PATH`** (carried). Lives at `C:\Program Files (x86)\Microsoft Visual Studio\Installer\` — prepend to `PATH` or use a Developer Command Prompt.
10. **No git remote — local-only.** `bd dolt push` and `git push` from the bd session-close protocol will fail; skip them.

Carried-over invariant **superseded** by lever 2: the prior handoff's "true single-file unrealistic" point listed ANGLE among the unavoidable platform deps. ANGLE is now gone. Skia + HarfBuzz + e_sqlite3 remain. e_sqlite3 is the next static-link candidate (d0q lever 1).

## Start here

```bash
bd ready                                                          # 8 beads with no blockers
git log --oneline -8                                              # 6 commits + 2 prior
dotnet build -c Debug && bin/Debug/net9.0/Stickies.exe            # smoke test
PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH" \
  dotnet publish -r win-x64 -c Release                            # for AOT measurement
```

## Sequencing (suggested)

`Stickies-94r` → `Stickies-edb` → `Stickies-drm` → `Stickies-har` → `Stickies-d0q` (next lever) → `Stickies-jl1`.

**Why:** `94r` (MSI installer) is the last shipping concern for v1; everything else is enhancement. Then `edb` (image paste) — concrete user value, lights up Skia. Then `drm` (inline markdown) — leverages text rendering already shipping. Then `har` (better color picker) — visible UX polish. Then `d0q`'s next lever (static-link sqlite, ~−1 MB net). `jl1` (peel animation) is the dessert — design is fully specified in the bead notes, but it needs the `RotateTransform`/perspective workaround in Avalonia and is the most YAGNI-tinged of the bunch. Also note the FTS5 / recycle-bin path: `4cm` and `usm` are both gated on a manager-window UX that hasn't been brainstormed yet — don't pick them up cold.

## New this session

- **Visual brainstorming with `superpowers:brainstorming`** worked well for UX surface decisions (right-click vs hover, swatch row vs submenu, peel orientation). Iteration tip learned: when showing a "fixed" version, include the broken version side-by-side so the user can confirm what changed.
- **`superpowers/brainstorm/`** mockup files persist (project-dir mode). Recent sessions are in `.superpowers/brainstorm/` (gitignored). `Stickies-jl1`'s notes reference specific mockup files for future-us; don't delete them blindly.
- **`bd remember` over MEMORY.md** — used for the size baseline. Query with `bd memories size`.

## Key artifacts

- Project conventions: [CLAUDE.md](../../CLAUDE.md)
- Peel animation spec (frozen design + iteration log): `bd show Stickies-jl1`
- Color picker UX revisit: `bd show Stickies-har`

## Out of scope (tempting but skip)

- **`Stickies-4cm` FTS5 search** — still gated on a manager/search window UX. No movement this session.
- **`Stickies-usm` recycle bin** — same gating as 4cm.
- **`Stickies-jl1` peel animation** — mid-scope and needs raw Skia or perspective-matrix workaround in Avalonia. Spec is locked, but pick it after shipping concerns (`94r`).
- **Static-link SQLite (d0q lever 1)** — ~−1 MB net but invasive MSBuild work (vendoring sqlite3.c, `cl.exe` invocation in `LinkNative`). Not worth without a clear shipping push.
