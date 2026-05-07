#!/bin/bash
# Assemble Stickies-${VERSION}-x86_64.AppImage from a published linux-x64 build.
# Must run on Linux (uses linuxdeploy + ImageMagick `convert`).
#
# Usage:
#   dotnet publish -c Release -r linux-x64
#   VERSION=0.2.0 build/linux/package.sh
set -euo pipefail

VERSION="${VERSION:-0.0.0}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/bin/Release/net9.0/linux-x64/publish"
SRC="$ROOT/Assets/stickies.png"
DESKTOP="$ROOT/build/linux/Stickies.desktop"

if [ ! -d "$PUBLISH" ]; then
  echo "Publish output not found: $PUBLISH" >&2
  echo "Run: dotnet publish -c Release -r linux-x64" >&2
  exit 1
fi

if ! command -v linuxdeploy >/dev/null 2>&1; then
  echo "linuxdeploy not on PATH. Install from https://github.com/linuxdeploy/linuxdeploy/releases" >&2
  exit 1
fi

if ! command -v convert >/dev/null 2>&1; then
  echo "ImageMagick 'convert' not on PATH (needed to resize icon)." >&2
  exit 1
fi

OUT="$ROOT/build/linux/out"
APPDIR="$OUT/AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# 1. Copy the published binaries to AppDir/usr/bin/.
cp -r "$PUBLISH/." "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/Stickies"

# 2. Resize 1024² master to 256² for the AppImage icon.
ICON256="$APPDIR/usr/share/icons/hicolor/256x256/apps/Stickies.png"
convert "$SRC" -resize 256x256 "$ICON256"

# AppImage runtime expects icon + .desktop at AppDir root too.
cp "$ICON256" "$APPDIR/Stickies.png"
cp "$DESKTOP" "$APPDIR/usr/share/applications/Stickies.desktop"
cp "$DESKTOP" "$APPDIR/Stickies.desktop"

# 3. Build the AppImage. linuxdeploy auto-generates AppRun.
cd "$OUT"
OUTPUT="Stickies-$VERSION-x86_64.AppImage" linuxdeploy --appdir AppDir --output appimage

echo "Built: $OUT/Stickies-$VERSION-x86_64.AppImage"
