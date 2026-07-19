#!/usr/bin/env bash
#
# Builds macOS .app bundles and .dmg disk images for BoltZip (Apple Silicon + Intel).
# Runs on macOS only (uses sips/iconutil/hdiutil) — see .github/workflows/release.yml.
#
set -euo pipefail

VERSION="0.1.0"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
mkdir -p dist

make_icns() {
    local png="src/BoltZip.App/Assets/boltzip.png"
    local iconset="$1/boltzip.iconset"
    mkdir -p "$iconset"
    for s in 16 32 64 128 256 512; do
        sips -z "$s" "$s" "$png" --out "$iconset/icon_${s}x${s}.png" >/dev/null
        d=$((s * 2))
        sips -z "$d" "$d" "$png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$2"
}

for RID in osx-arm64 osx-x64; do
    ARCH="${RID#osx-}"
    echo "==> Building $RID"

    dotnet publish src/BoltZip.App/BoltZip.App.csproj -c Release -r "$RID" \
        --self-contained true -p:PublishSingleFile=false -o "publish/app-$RID"
    dotnet publish src/BoltZip.Cli/BoltZip.Cli.csproj -c Release -r "$RID" \
        --self-contained true -p:PublishSingleFile=true -o "publish/cli-$RID"

    STAGE="publish/stage-$ARCH"
    APP="$STAGE/BoltZip.app"
    rm -rf "$STAGE"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

    cp -R "publish/app-$RID/." "$APP/Contents/MacOS/"
    cp "publish/cli-$RID/bz" "$APP/Contents/MacOS/bz"
    chmod +x "$APP/Contents/MacOS/BoltZipTool" "$APP/Contents/MacOS/bz"

    make_icns "publish/tmp-$ARCH" "$APP/Contents/Resources/boltzip.icns"

    cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>BoltZip</string>
    <key>CFBundleDisplayName</key><string>BoltZip</string>
    <key>CFBundleIdentifier</key><string>com.briansantacruz.boltzip</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleExecutable</key><string>BoltZipTool</string>
    <key>CFBundleIconFile</key><string>boltzip</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

    ln -sf /Applications "$STAGE/Applications"

    DMG="dist/BoltZip-$VERSION-$ARCH.dmg"
    rm -f "$DMG"
    hdiutil create -volname "BoltZip" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
    echo "==> Created $DMG"
done
