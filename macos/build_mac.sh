#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="StealAnEgg_AFK_Bot.app"
OUT_DIR="${1:-$SCRIPT_DIR/build}"
APP_DIR="$OUT_DIR/$APP_NAME"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"

mkdir -p "$MACOS_DIR"

echo "Compiling arm64 slice..."
swiftc -O -target arm64-apple-macos14.0 \
    -framework Cocoa \
    -framework ScreenCaptureKit \
    -framework Carbon \
    -framework ApplicationServices \
    "$SCRIPT_DIR/main.swift" \
    -o "$OUT_DIR/StealAnEggBot_arm64"

echo "Compiling x86_64 slice..."
swiftc -O -target x86_64-apple-macos14.0 \
    -framework Cocoa \
    -framework ScreenCaptureKit \
    -framework Carbon \
    -framework ApplicationServices \
    "$SCRIPT_DIR/main.swift" \
    -o "$OUT_DIR/StealAnEggBot_x86_64"

echo "Creating Universal Binary..."
lipo -create -output "$MACOS_DIR/StealAnEggBot" \
    "$OUT_DIR/StealAnEggBot_arm64" \
    "$OUT_DIR/StealAnEggBot_x86_64"

rm -f "$OUT_DIR/StealAnEggBot_arm64" "$OUT_DIR/StealAnEggBot_x86_64"
cp "$SCRIPT_DIR/Info.plist" "$CONTENTS_DIR/Info.plist"
chmod +x "$MACOS_DIR/StealAnEggBot"

# Ad-hoc sign so Gatekeeper/TCC permissions bind cleanly to the app bundle
codesign --force --deep --sign - "$APP_DIR"

echo "Built macOS Universal App: $APP_DIR"
