#!/usr/bin/env bash
#
# BoltZip installer for macOS.
#
#   curl -fsSL https://bsantacruzms.github.io/Bzip/install-mac.sh | bash
#
# Downloads the correct build for your Mac, verifies its checksum, installs it into
# /Applications, and clears the download quarantine flag so it opens normally.
#
# Why the quarantine step: BoltZip is free and open source and is not notarized by Apple
# (notarization requires a paid Apple Developer account), so macOS blocks it on first launch.
# Running this script is you explicitly choosing to trust the download, which is the same
# decision Homebrew casks make on your behalf. The checksum is verified first.
#
# Review it before running, as you should with any install script:
#   curl -fsSL https://bsantacruzms.github.io/Bzip/install-mac.sh | less
#
set -euo pipefail

REPO="bsantacruzms/Bzip"
API="https://api.github.com/repos/$REPO/releases/latest"
APP_DIR="/Applications"
DRY_RUN="${DRY_RUN:-0}"

for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=1 ;;
        -h|--help) sed -n '2,18p' "$0"; exit 0 ;;
    esac
done

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "this installer is for macOS. On Linux use install.sh instead."
command -v curl >/dev/null 2>&1 || die "curl is required."

case "$(uname -m)" in
    arm64) arch=arm64; label="Apple silicon" ;;
    x86_64) arch=x64; label="Intel" ;;
    *) die "unsupported architecture: $(uname -m)" ;;
esac

say "BoltZip installer for macOS"
say "  mac:      $label ($(uname -m))"

json="$(curl -fsSL "$API")" || die "could not reach the GitHub release API."
version="$(printf '%s' "$json" | sed -n 's/.*"tag_name" *: *"\([^"]*\)".*/\1/p' | head -n1)"
[ -n "$version" ] || die "could not determine the latest version."
say "  version:  $version"

url="$(printf '%s' "$json" \
    | tr ',' '\n' \
    | sed -n 's/.*"browser_download_url" *: *"\([^"]*\)".*/\1/p' \
    | grep -E "\-${arch}\.dmg$" \
    | head -n1)"
[ -n "$url" ] || die "no ${arch} disk image found in release $version."
say "  package:  $(basename "$url")"

if [ "$DRY_RUN" = "1" ]; then
    say ""
    say "dry run: would download $url"
    exit 0
fi

tmp="$(mktemp -d)"
cleanup() {
    [ -n "${mounted:-}" ] && hdiutil detach "$mounted" -quiet >/dev/null 2>&1 || true
    rm -rf "$tmp"
}
trap cleanup EXIT

dmg="$tmp/$(basename "$url")"
say ""
say "Downloading ..."
curl -fsSL --proto '=https' --tlsv1.2 -o "$dmg" "$url" || die "download failed."

# ---- verify before trusting it ----
sums_url="$(printf '%s' "$json" \
    | tr ',' '\n' \
    | sed -n 's/.*"browser_download_url" *: *"\([^"]*\)".*/\1/p' \
    | grep -E 'SHA256SUMS\.txt$' | head -n1)"
if [ -n "$sums_url" ]; then
    curl -fsSL --proto '=https' --tlsv1.2 -o "$tmp/SHA256SUMS.txt" "$sums_url" \
        || die "could not download SHA256SUMS.txt."
    name="$(basename "$url")"
    expected="$(grep -E "  ?\*?${name}\$" "$tmp/SHA256SUMS.txt" | awk '{print $1}' | head -n1)"
    [ -n "$expected" ] || die "no checksum listed for $name; refusing to install."
    actual="$(shasum -a 256 "$dmg" | awk '{print $1}')"
    [ "$expected" = "$actual" ] || die "checksum mismatch for $name. Refusing to install."
    say "  verified: SHA256 OK"
else
    say "  note:     this release predates published checksums, skipping verification."
fi

say "Installing to $APP_DIR ..."
mounted="$(hdiutil attach "$dmg" -nobrowse -quiet -mountrandom /tmp | grep -o '/tmp/[^ ]*' | tail -n1)"
[ -n "$mounted" ] || die "could not mount the disk image."

src="$mounted/BoltZip.app"
[ -d "$src" ] || die "BoltZip.app not found inside the disk image."

SUDO=""
if [ ! -w "$APP_DIR" ]; then
    SUDO="sudo"
    say "  (you may be prompted for your password to write to $APP_DIR)"
fi

$SUDO rm -rf "$APP_DIR/BoltZip.app"
$SUDO cp -R "$src" "$APP_DIR/BoltZip.app"
hdiutil detach "$mounted" -quiet; mounted=""

# Clear the quarantine flag: without this macOS refuses to open a non-notarized app and
# offers only "Move to Trash". Newer macOS also removed the Control-click override.
$SUDO xattr -dr com.apple.quarantine "$APP_DIR/BoltZip.app" 2>/dev/null || true

# Make the bz CLI available on PATH, best effort.
for bindir in /usr/local/bin /opt/homebrew/bin; do
    if [ -d "$bindir" ] && [ -x "$APP_DIR/BoltZip.app/Contents/MacOS/bz" ]; then
        $SUDO ln -sf "$APP_DIR/BoltZip.app/Contents/MacOS/bz" "$bindir/bz" 2>/dev/null && break
    fi
done

say ""
say "Installed: $APP_DIR/BoltZip.app"
if command -v bz >/dev/null 2>&1; then
    say "CLI:       $(bz --version 2>/dev/null || echo 'bz')"
else
    say "CLI:       $APP_DIR/BoltZip.app/Contents/MacOS/bz (add it to your PATH if you want 'bz')"
fi
say ""
say "Open it from Launchpad or Applications. No Gatekeeper prompt: the quarantine flag is cleared."
