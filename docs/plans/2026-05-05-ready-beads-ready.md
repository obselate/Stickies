# Stickies v0 working — picking from ready beads

**Date:** 2026-05-05
**Status:** v0 sticky-note app is working and committed (multi-note + AOT 15.5MB). Next session picks the next feature from 10 ready beads.

## Commits landed this session

| Commit | Bead | What |
|---|---|---|
| `cbeee9b` | — | Initial scaffold: single yellow note, drag, SQLite WAL persistence (after wiping prior GSD-only history). |
| `b023c39` | — | `bd init` — beads tracker installed, hooks registered. |
| `af8b271` | — | Multi-note: schema migration adds `x/y/width/height/deleted_at`; `Ctrl+N` spawns; `Ctrl+D` soft-deletes; bottom-right resize grip; debounced 400ms position/size + text saves; save-on-Opened normalizes DB to actual on-screen position. |
| `994c994` | `Stickies-xc4` (closed) | AOT publish working. 15.5MB exe (under 25MB target). Replaced Fluent theme with Simple, dropped Inter font for system Segoe UI, set `IlcOptimizationPreference=Size` + `IlcGenerateStackTraceData=false` + `Debugger/EventSource/MetadataUpdater Support=false` + `UseSystemResourceKeys=true`. |
| `e8198f2` | `Stickies-d0q` | CLAUDE.md captures speed+size as a permanent priority (not a one-time target). |

## Design invariants (don't re-decide)

1. **No GSD machinery.** `.planning/` and the GSD enforcement section in CLAUDE.md were deliberately wiped — every commit before `cbeee9b` was GSD documents only. Ceremony slowed the user down. `bd` is the only tracker now.
2. **Code-behind only — no MVVM.** Locked in CLAUDE.md. `App.Store` is a static singleton (`new()` per-call connection, AOT-clean). Don't introduce DI, view models, or reactive frameworks.
3. **Speed and size are permanent priorities, not one-time targets.** Every change is measured against current size + cold-start. See `Stickies-d0q`. Going under the budget is always preferred to staying at it.
4. **Avalonia 11.3.* — never 12.x for v1.** Breaking changes (`SystemDecorations` rename, clipboard, `TopLevel`) would force a re-prototype.
5. **Closing a note window does NOT delete the note.** It hides until next launch. Soft-delete is `Ctrl+D` only. Without a manager window, "closed but not deleted" notes are inaccessible until restart — that's the v0.5 trade-off, see `Stickies-rt1` and `Stickies-usm`.
6. **AOT publish requires `vswhere.exe` on `PATH`.** It lives at `C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe` but is not on `PATH` by default on this box. Either run from a Developer Command Prompt or prepend the directory before `dotnet publish`.
7. **No git remote — local-only for now.** `bd dolt push` and `git push` (per the bd session-close protocol) will fail. Skip them; the user has confirmed local-only is intended.
8. **True single-file is unrealistic.** Avalonia's native deps (Skia 9MB, ANGLE 5.2MB, HarfBuzz 1.8MB) cannot reasonably be static-linked. Realistic shippable: 1 exe + 3 platform DLLs + e_sqlite3.dll. SQLite is the only one we can statically link (see `Stickies-d0q` lever 1).

## Start here

```bash
bd ready                                     # see the 10 ready beads
git log --oneline -6                         # five commits + bd init
dotnet build -c Debug && dotnet bin/Debug/net9.0/Stickies.dll   # smoke test the app
# For AOT publish:  PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH" dotnet publish -r win-x64 -c Release
```

## Sequencing (suggested)

`Stickies-y12` → `Stickies-rt1` → `Stickies-b06` → `Stickies-8f3` → `Stickies-d0q` levers → `Stickies-94r`.

Why: single-instance (`y12`) is a correctness gap — running the app twice today gives duplicate windows on every existing note, which is broken. Once that's fixed, the right-click menu (`rt1`) consolidates Pin/New/Delete and is a prerequisite UX surface for the always-on-top toggle (`b06`). Global hotkey (`8f3`) needs the single-instance fix to be useful (otherwise the hotkey would just spawn a new app instance instead of hitting the running one). Size-shaving (`d0q`) is incremental and can interleave at any point. MSI (`94r`) is shipping, last.

## Out of scope (tempting but skip)

- **`Stickies-265` per-note color** — looks small but needs schema migration + UI surface; pair with `rt1` (context menu) when it lands rather than as a standalone.
- **`Stickies-4cm` FTS5 search** — pointless until there's a manager/search window; needs UX design first.
- **`Stickies-lwc` export to .md** — useful but no demand yet; user hasn't asked.
- **`Stickies-usm` recycle bin** — same: needs a manager window. Soft-delete already works at the DB level.

## Key artifacts

- Project conventions: [CLAUDE.md](../../CLAUDE.md)
- Bead inventory: `bd list` (10 open, 1 closed `Stickies-xc4`)
