#!/usr/bin/env bash
#
# Builds macOS .app bundles and .dmg disk images for BoltZip (Apple Silicon + Intel).
# Runs on macOS only (uses sips/iconutil/hdiutil) — see .github/workflows/release.yml.
#
set -euo pipefail

VERSION="1.1.3"
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
    <key>CFBundleIdentifier</key><string>com.boltzip.app</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleExecutable</key><string>BoltZipTool</string>
    <key>CFBundleIconFile</key><string>boltzip</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>CFBundleDocumentTypes</key>
    <array>
        <dict>
            <key>CFBundleTypeName</key><string>BoltZip Archive</string>
            <key>CFBundleTypeRole</key><string>Editor</string>
            <key>LSHandlerRank</key><string>Owner</string>
            <key>CFBundleTypeIconFile</key><string>boltzip</string>
            <key>LSItemContentTypes</key>
            <array><string>com.boltzip.app.archive</string></array>
            <key>CFBundleTypeExtensions</key>
            <array><string>bz</string></array>
        </dict>
        <dict>
            <key>CFBundleTypeName</key><string>Archive</string>
            <key>CFBundleTypeRole</key><string>Editor</string>
            <key>LSHandlerRank</key><string>Alternate</string>
            <key>CFBundleTypeIconFile</key><string>boltzip</string>
            <key>CFBundleTypeExtensions</key>
            <array>
                <string>zip</string><string>7z</string><string>rar</string>
                <string>tar</string><string>gz</string><string>tgz</string>
                <string>bz2</string><string>tbz2</string><string>zst</string>
                <string>tzst</string><string>xz</string><string>txz</string>
                <string>br</string><string>lz</string>
            </array>
        </dict>
    </array>
    <key>UTExportedTypeDeclarations</key>
    <array>
        <dict>
            <key>UTTypeIdentifier</key><string>com.boltzip.app.archive</string>
            <key>UTTypeDescription</key><string>BoltZip Archive</string>
            <key>UTTypeIconFile</key><string>boltzip</string>
            <key>UTTypeConformsTo</key>
            <array><string>public.data</string><string>public.archive</string></array>
            <key>UTTypeTagSpecification</key>
            <dict>
                <key>public.filename-extension</key><array><string>bz</string></array>
            </dict>
        </dict>
    </array>
</dict>
</plist>
PLIST

    ln -sf /Applications "$STAGE/Applications"

    # BoltZip is not signed with a paid Apple Developer ID, so the first launch needs one
    # extra click. Spell that out inside the disk image where people will actually see it.
    cat > "$STAGE/How to open BoltZip.txt" <<'TXT'
Opening BoltZip for the first time
==================================

1. Drag BoltZip to the Applications folder.
2. In Applications, RIGHT-CLICK (or Control-click) BoltZip and choose "Open".
3. Click "Open" in the dialog that appears.

You only need to do this once. Afterwards BoltZip opens normally.

Why is this necessary?
BoltZip is free and open source and is not signed with a paid Apple Developer ID,
so macOS asks you to confirm the first launch. Double-clicking the app the first
time shows a warning instead of opening it, which is why the right-click matters.

Still blocked?
Open System Settings > Privacy & Security, scroll down, and click "Open Anyway".
Or run this in Terminal:

    xattr -dr com.apple.quarantine /Applications/BoltZip.app

Source code and checksums: https://github.com/bsantacruzms/Bzip/releases
TXT

    # macOS refuses to launch a bundle whose signature is missing or invalid and reports
    # "BoltZip is damaged and can't be opened", which is fatal on Apple silicon. Publishing
    # and then editing the bundle (adding the icon, Info.plist and the bz binary) invalidates
    # whatever signature dotnet applied, so sign the finished bundle here, last.
    #
    # This is an ad-hoc signature ("-"), which makes the app runnable everywhere. It is not a
    # Developer ID signature, so Gatekeeper still asks the user to confirm the first launch
    # (right-click, then Open). Removing that prompt entirely requires a paid Apple Developer
    # account to sign with a Developer ID and notarize.
    codesign --force --deep --sign - "$APP"
    codesign --verify --deep --strict "$APP"
    echo "==> Signed (ad-hoc) $APP"

    DMG="dist/BoltZip-$VERSION-$ARCH.dmg"
    rm -f "$DMG"
    hdiutil create -volname "BoltZip" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
    echo "==> Created $DMG"
done
