# Stickies

A reliable, lean, no-nonsense sticky-notes app. Pure local storage, no accounts, no cloud sync, no AI integration, no formatting toolbars. Just text on yellow squares that survive crashes, start fast, and don't change behind your back.

Native binaries for **Windows**, **macOS** (Apple Silicon), and **Linux** (x64).

## Install

Grab the latest release from [Releases](https://github.com/obselate/Stickies/releases).

### Windows

Download `Stickies-X.Y.Z-win-x64.msi` and run it. Per-user install (no UAC prompt). Or grab the `.zip` and run `Stickies.exe` directly.

### macOS (Apple Silicon)

Download `Stickies-X.Y.Z-osx-arm64.dmg`, mount it, drag `Stickies.app` to `/Applications`.

The build is unsigned, so on first launch macOS Gatekeeper will block it. Right-click `Stickies.app` → **Open** → confirm. After that it launches normally.

### Linux (x64)

Download `Stickies-X.Y.Z-x86_64.AppImage`, then:

```bash
chmod +x Stickies-*.AppImage
./Stickies-*.AppImage
```

The AppImage runtime needs FUSE; on most distros `libfuse2` is preinstalled. If it isn't: `sudo apt install libfuse2` (Debian/Ubuntu) or your distro's equivalent.

## Use

| | |
|---|---|
| **Spawn a new note** | Ctrl+Shift+S (Windows / Linux) · ⌘⇧S (macOS) |
| **New from a note** | Right-click → New note · Ctrl+N |
| **Pin on top** | Right-click → Pin on top |
| **Change color** | Right-click → swatch · `…` for custom |
| **Delete** | Right-click → Delete · Ctrl+Delete |

Notes auto-save as you type. Position and size persist per note. Soft-delete only — there's no recycle bin UI yet, but nothing's gone from the database.

### Global hotkey on Linux

The hotkey is registered via X11 `XGrabKey`. On pure Wayland (no XWayland) the hotkey silently no-ops; the rest of the app works fine. GNOME and KDE on most distros default to a session that exposes XWayland, so the hotkey works there.

## Build from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
git clone https://github.com/obselate/Stickies
cd Stickies
dotnet publish -c Release -r win-x64    # or osx-arm64, linux-x64
```

The published binary lands in `bin/Release/net9.0/<rid>/publish/`. To package per platform:

- Windows MSI: `dotnet build installer/Stickies.wixproj -c Release -p:Version=0.0.0`
- macOS .dmg: `VERSION=0.0.0 build/mac/package.sh` (run on macOS)
- Linux AppImage: `VERSION=0.0.0 build/linux/package.sh` (run on Linux; needs `linuxdeploy` and ImageMagick)

NativeAOT does not cross-compile across host OS, so each native binary must be built on the matching host (or matching CI runner).

## Stack

Avalonia 11.3 · .NET 9 NativeAOT · Microsoft.Data.Sqlite. No view-models, no MVVM framework, no AI, no telemetry. Targets: cold start <300ms, <60MB RAM with 10 notes, exe <25MB.

