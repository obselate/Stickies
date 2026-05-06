# Stickies

A reliable, lean, no-nonsense sticky-notes app for Windows. Pure local storage, no accounts, no cloud sync, no AI integration, no formatting toolbars. Just text on yellow squares that survive crashes, start fast, and don't change behind your back.

**Core value:** Notes never get lost, the app starts instantly, and nothing ever calls home.

## Constraints

- **Tech stack:** Avalonia 11.* + .NET 9 NativeAOT, `Microsoft.Data.Sqlite` 9.*. Adding a NuGet package needs real justification — every package threatens binary size and start time.
- **Architecture:** Code-behind only. `x:Class` + `InitializeComponent` + event handlers. No view models, no MVVM framework. AOT-friendly; nothing to learn beyond C# + XAML.
- **Performance targets:** Cold start <300ms to first paint, <60MB RAM with 10 notes, single-file exe <25MB.
- **Platform:** Windows 10/11 x64 only for v1. Win32 P/Invoke is fine where it earns its keep (global hotkey, etc.).
- **Privacy:** Zero network calls in the shipped binary, ever.
- **Build:** `PublishAot=true`, `TrimMode=full`, `InvariantGlobalization=true`. Don't regress to reflection-only paths.

## Stack notes (reference)

Locked versions:

| Package | Version | Notes |
|---|---|---|
| `Avalonia` (+ Desktop, Themes.Fluent, Fonts.Inter) | `11.3.*` | Stay on 11.3 — Avalonia 12 has breaking changes (`SystemDecorations` rename, clipboard, `TopLevel`). |
| `Microsoft.Data.Sqlite` | `9.0.*` | Brings `SQLitePCLRaw.bundle_e_sqlite3`. AOT-clean. |
| `.NET SDK` | 9.0.x | NativeAOT mature on Windows. |

For MSI packaging (when we get there): WiX v5 as MSBuild SDK, `Scope="perUser"` (no UAC).

### Banned additions

- ReactiveUI / CommunityToolkit.Mvvm / Prism — code-behind only.
- EF Core / Dapper — raw `Microsoft.Data.Sqlite` is fine for one table.
- NHotkey / GlobalHotKey.Wpf — they drag in WPF; use `[LibraryImport] RegisterHotKey` directly.
- FuzzySharp / similarity libs — SQLite FTS5 trigram covers it.
- MSIX / Velopack / Squirrel / Inno — we want a plain MSI, no auto-update.
- Serilog / NLog / MEL — if logging is needed, one method writing to `%LOCALAPPDATA%\Stickies\log.txt`.
- Newtonsoft.Json — `System.Text.Json` source-gen if any JSON is needed.
- WebView / CefSharp / WebView2 — defeats the lean thesis.

### AOT-relevant patterns

- `LibraryImport` (not `DllImport`) for any P/Invoke.
- Public parameterless ctors on every `Window`; `x:DataType` on every binding scope; no runtime XAML loading.
- Multiple notes: `Mutex` + named pipe `Stickies.IPC` for single-instance dispatch. `ShutdownMode = OnExplicitShutdown` so closing the last note doesn't kill the process if a manager is up.
- Global hotkey: tiny invisible message-pump `Window`, hook via `Win32Properties.AddWndProcHookCallback`, intercept `WM_HOTKEY` (`0x0312`).
- Resize grip: small bottom-right `Border`, `Cursor="BottomRightCorner"`, `BeginResizeDrag(WindowEdge.SouthEast, e)`.
- Per-note position/size persistence: debounced `PositionChanged`/`SizeChanged` (the 400ms `DispatcherTimer` pattern in `MainWindow.axaml.cs`); validate against `Screens.All` before applying on startup.
- Soft-delete: `deleted_at` column; "active" view filters `WHERE deleted_at IS NULL`.
- Export: `TopLevel.GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(...)`.

## Conventions

To be filled in as patterns emerge.


<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:ca08a54f -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

## Session Completion

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase
   bd dolt push
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up** - Clear stashes, prune remote branches
6. **Verify** - All changes committed AND pushed
7. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
<!-- END BEADS INTEGRATION -->
