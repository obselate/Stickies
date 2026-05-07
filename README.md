# Stickies

Windows Sticky Notes that don't suck. That's it.

Native binaries for **Windows**, **macOS** (Apple Silicon), and **Linux** (x64).

## Install

Grab the latest release from [Releases](https://github.com/obselate/Stickies/releases).

### Windows

Download `Stickies-X.Y.Z-win-x64.msi` and run it. Per-user install (no UAC prompt). SmartScreen may show "Windows protected your PC" because the build isn't signed — click **More info** → **Run anyway**. Or grab the `.zip` and run `Stickies.exe` directly.

### macOS (Apple Silicon)

Download `Stickies-X.Y.Z-osx-arm64.dmg`, mount it, drag `Stickies.app` to `/Applications`.

The build is unsigned, so macOS Gatekeeper quarantines it on download. The reliable one-shot fix:

```bash
xattr -cr /Applications/Stickies.app
```

That clears the quarantine attribute and the app launches normally. (Without this, on Sequoia+ the GUI path is **System Settings → Privacy & Security → "Stickies was blocked" → Open Anyway**, but `xattr -cr` is faster and works on all macOS versions.)

### Linux (x64)

Download `Stickies-X.Y.Z-x86_64.AppImage`, then:

```bash
chmod +x Stickies-*.AppImage
./Stickies-*.AppImage
```

AppImage uses FUSE to self-mount at startup. Most desktop Linux distros ship with FUSE; if yours doesn't:

- Ubuntu 22.04 / Debian: `sudo apt install libfuse2`
- Ubuntu 24.04+: `sudo apt install libfuse2t64`
- Fedora / RHEL: `sudo dnf install fuse-libs`

If you can't install FUSE (e.g. a locked-down container), run the AppImage with `--appimage-extract-and-run` to extract and launch without mounting.

## Use

| | |
|---|---|
| **Spawn a new note (global hotkey)** | Ctrl+Shift+N (Windows / Linux) · ⌘⇧N (macOS) |
| **New from a note** | Ctrl+N (Windows / Linux) · ⌘N (macOS) |
| **Pin on top** | Right-click → Pin on top |
| **Change color** | Right-click → swatch · `…` for custom HSV picker |
| **Delete** | Ctrl+D (Windows / Linux) · ⌘D (macOS) |

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
