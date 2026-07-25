#!/usr/bin/env bash
#
# Installs the portable BoltZip build into your system (or user) prefix and links
# archive file types (.bz, .zip, .7z, .tar, .gz, .zst, .xz, ...) to open with BoltZip.
#
#   sudo ./install.sh     -> installs system-wide into /usr/local
#   ./install.sh          -> installs for the current user into ~/.local
#
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

if [ "$(id -u)" -eq 0 ]; then
    prefix="/usr/local"
    mimedir="/usr/share/mime"
else
    prefix="$HOME/.local"
    mimedir="$HOME/.local/share/mime"
fi

libdir="$prefix/lib/boltzip"
bindir="$prefix/bin"
appdir="$prefix/share/applications"
icondir="$prefix/share/icons/hicolor/256x256/apps"

echo "Installing BoltZip into $prefix ..."
mkdir -p "$libdir" "$bindir" "$appdir" "$icondir" "$mimedir/packages"

install -m 755 "$here/BoltZipTool" "$libdir/BoltZipTool"
install -m 755 "$here/bz" "$libdir/bz"
ln -sf "$libdir/BoltZipTool" "$bindir/boltzip"
ln -sf "$libdir/bz" "$bindir/bz"
install -m 644 "$here/boltzip.png" "$icondir/boltzip.png"

# Desktop entry with absolute paths for this install location (links the file types).
sed -e "s|^Exec=.*|Exec=$bindir/boltzip %F|" \
    -e "s|^TryExec=.*|TryExec=$bindir/boltzip|" \
    -e "s|^Icon=.*|Icon=$icondir/boltzip.png|" \
    "$here/boltzip.desktop" > "$appdir/boltzip.desktop"
chmod 644 "$appdir/boltzip.desktop"

# Register the native .bz MIME type.
install -m 644 "$here/boltzip-mime.xml" "$mimedir/packages/boltzip.xml"

command -v update-mime-database >/dev/null 2>&1 && update-mime-database "$mimedir" 2>/dev/null || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$appdir" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t "$prefix/share/icons/hicolor" 2>/dev/null || true

echo
echo "Done. BoltZip and the 'bz' CLI are installed."
echo "Archives now show BoltZip under 'Open with' in your file manager."
case ":$PATH:" in
    *":$bindir:"*) : ;;
    *) echo "Note: add $bindir to your PATH to run 'bz' from any terminal." ;;
esac
