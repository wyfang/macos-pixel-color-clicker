#!/bin/zsh
set -euo pipefail

ROOT_DIR="${0:A:h}"
OUTPUT_DIR="$ROOT_DIR/outputs"
APP_DIR="$OUTPUT_DIR/像素变化点击器.app"
CACHE_DIR="$ROOT_DIR/work/build-cache"

cd "$ROOT_DIR"
mkdir -p "$CACHE_DIR/tmp" "$CACHE_DIR/clang" "$CACHE_DIR/swiftpm"
export TMPDIR="$CACHE_DIR/tmp"
export CLANG_MODULE_CACHE_PATH="$CACHE_DIR/clang"
export SWIFTPM_MODULECACHE_OVERRIDE="$CACHE_DIR/clang"
export SWIFTPM_CUSTOM_CACHE_PATH="$CACHE_DIR/swiftpm"
swift build --disable-sandbox -c release

mkdir -p "$APP_DIR/Contents/MacOS"
cp ".build/release/PixelWatcher" "$APP_DIR/Contents/MacOS/PixelWatcher"

cat > "$APP_DIR/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key><string>zh_CN</string>
    <key>CFBundleDisplayName</key><string>像素变化点击器</string>
    <key>CFBundleExecutable</key><string>PixelWatcher</string>
    <key>CFBundleIdentifier</key><string>com.local.pixelwatcher</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>像素变化点击器</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>1.0</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSScreenCaptureUsageDescription</key><string>用于读取用户选择位置的屏幕像素颜色。</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP_DIR"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$OUTPUT_DIR/像素变化点击器.zip"
echo "$APP_DIR"
