# Stickies

Minimal sticky notes MVP — Avalonia 11, NativeAOT, SQLite. No ReactiveUI, no MVVM toolkits, code-behind only.

## Run

```sh
dotnet run
```

## Publish (single-file AOT exe)

```sh
dotnet publish -c Release -r win-x64
```

Output: `bin/Release/net9.0/win-x64/publish/Stickies.exe` (~15 MB single file, no .NET runtime needed).

## Storage

Notes persist to `%LOCALAPPDATA%\Stickies\notes.db` (SQLite, WAL mode). Auto-save fires 400ms after the last keystroke.

## Layout

- `Program.cs` — entry point, registers Inter font
- `App.axaml` / `.axaml.cs` — app shell + Fluent theme (for TextBox templates)
- `MainWindow.axaml` / `.axaml.cs` — borderless yellow note window with drag handle
- `NoteStore.cs` — SQLite wrapper, single-note MVP

## Next layers

- Resize grip
- Multi-note (one window per row)
- Global hotkey for new note (`RegisterHotKey` via P/Invoke)
- Per-note color picker
- Always-on-top toggle
