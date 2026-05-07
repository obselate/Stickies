#!/bin/bash
# Assemble Stickies.app and Stickies-${VERSION}-osx-arm64.dmg from a published
# Apple Silicon build. Must run on macOS (uses sips, iconutil, hdiutil).
#
# Usage:
#   dotnet publish -c Release -r osx-arm64
#   VERSION=0.2.0 build/mac/package.sh
set -euo pipefail

VERSION="${VERSION:-0.0.0}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/bin/Release/net9.0/osx-arm64/publish"
SRC="$ROOT/Assets/stickies.png"

if [ ! -d "$PUBLISH" ]; then
  echo "Publish output not found: $PUBLISH" >&2
  echo "Run: dotnet publish -c Release -r osx-arm64" >&2
  exit 1
fi

if [ ! -f "$SRC" ]; then
  echo "Icon master not found: $SRC" >&2
  exit 1
fi

OUT="$ROOT/build/mac/out"
mkdir -p "$OUT"

# 1. Generate .icns from the 1024² master.
ICONSET="$OUT/Stickies.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
sips -z 16 16     "$SRC" --out "$ICONSET/icon_16x16.png"      >/dev/null
sips -z 32 32     "$SRC" --out "$ICONSET/icon_16x16@2x.png"   >/dev/null
sips -z 32 32     "$SRC" --out "$ICONSET/icon_32x32.png"      >/dev/null
sips -z 64 64     "$SRC" --out "$ICONSET/icon_32x32@2x.png"   >/dev/null
sips -z 128 128   "$SRC" --out "$ICONSET/icon_128x128.png"    >/dev/null
sips -z 256 256   "$SRC" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256 256   "$SRC" --out "$ICONSET/icon_256x256.png"    >/dev/null
sips -z 512 512   "$SRC" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512 512   "$SRC" --out "$ICONSET/icon_512x512.png"    >/dev/null
sips -z 1024 1024 "$SRC" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
ICNS="$OUT/Stickies.icns"
iconutil -c icns "$ICONSET" -o "$ICNS"
rm -rf "$ICONSET"

# 2. Assemble .app bundle.
APP="$OUT/Stickies.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
mkdir -p "$APP/Contents/Resources"

cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Stickies"
cp "$ICNS" "$APP/Contents/Resources/Stickies.icns"
sed "s/\${VERSION}/$VERSION/g" "$ROOT/build/mac/Info.plist" > "$APP/Contents/Info.plist"

# 3. Stage the DMG layout: Stickies.app, /Applications symlink for drag-install,
#    .VolumeIcon.icns at root for the volume's Finder icon.
STAGE="$OUT/dmg-stage"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/Stickies.app"
ln -s /Applications "$STAGE/Applications"
cp "$ICNS" "$STAGE/.VolumeIcon.icns"

# 4. Build a writable DMG, set the volume's custom-icon Finder flag, then
#    convert to a compressed read-only DMG. SetFile -a C is what tells Finder
#    to use .VolumeIcon.icns; without it the disk image shows the default icon.
DMG="$OUT/Stickies-$VERSION-osx-arm64.dmg"
RW_DMG="$OUT/Stickies-rw.dmg"
MOUNT="$OUT/mount"
rm -f "$DMG" "$RW_DMG"
rm -rf "$MOUNT"

hdiutil create -volname Stickies -srcfolder "$STAGE" -ov -format UDRW "$RW_DMG"
hdiutil attach "$RW_DMG" -mountpoint "$MOUNT" -nobrowse
SetFile -a C "$MOUNT" 2>/dev/null || true
hdiutil detach "$MOUNT"
hdiutil convert "$RW_DMG" -format UDZO -ov -o "$DMG"
rm -f "$RW_DMG"

echo "Built: $DMG"
