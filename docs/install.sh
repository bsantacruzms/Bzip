#!/usr/bin/env sh
#
# BoltZip installer for Linux.
#
#   curl -fsSL https://bsantacruzms.github.io/Bzip/install.sh | sh
#
# Downloads the latest release and installs the best package for your system: .deb on
# Debian/Ubuntu, .rpm on Fedora/RHEL/openSUSE, otherwise the portable tarball into
# ~/.local (no root required). Re-run it any time to upgrade.
#
# Review before running, as you should with any install script:
#   curl -fsSL https://bsantacruzms.github.io/Bzip/install.sh | less
#
# Options:  --portable   install into ~/.local without root, even on Debian/Fedora
#           --dry-run    show what would be downloaded, then stop
#
set -eu

REPO="bsantacruzms/Bzip"
API="https://api.github.com/repos/$REPO/releases/latest"
DRY_RUN="${DRY_RUN:-0}"
FORCE_PORTABLE="${FORCE_PORTABLE:-0}"

for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=1 ;;
        --portable) FORCE_PORTABLE=1 ;;
        -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
    esac
done

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

command -v curl >/dev/null 2>&1 || die "curl is required."

# ---- architecture ----
machine="$(uname -m)"
case "$machine" in
    x86_64|amd64)  deb_arch=amd64; rpm_arch=x86_64;  tar_arch=x64 ;;
    aarch64|arm64) deb_arch=arm64; rpm_arch=aarch64; tar_arch=arm64 ;;
    *) die "unsupported architecture: $machine (BoltZip ships x86_64 and arm64)" ;;
esac

# ---- package manager ----
kind=tar
if [ "$FORCE_PORTABLE" = "1" ]; then
    kind=tar
elif command -v dpkg >/dev/null 2>&1 && command -v apt-get >/dev/null 2>&1; then
    kind=deb
elif command -v rpm >/dev/null 2>&1; then
    kind=rpm
fi

say "BoltZip installer"
say "  architecture: $machine"
say "  package type: $kind"

# ---- pick the asset from the latest release ----
json="$(curl -fsSL "$API")" || die "could not reach the GitHub release API."
version="$(printf '%s' "$json" | sed -n 's/.*"tag_name" *: *"\([^"]*\)".*/\1/p' | head -n1)"
[ -n "$version" ] || die "could not determine the latest version."
say "  version:      $version"

case "$kind" in
    deb) pattern="_${deb_arch}\.deb" ;;
    rpm) pattern="\.${rpm_arch}\.rpm" ;;
    tar) pattern="linux-${tar_arch}\.tar\.gz" ;;
esac

url="$(printf '%s' "$json" \
    | tr ',' '\n' \
    | sed -n 's/.*"browser_download_url" *: *"\([^"]*\)".*/\1/p' \
    | grep -E "$pattern" \
    | head -n1)"
[ -n "$url" ] || die "no $kind package found for $machine in release $version."
say "  package:      $(basename "$url")"

if [ "$DRY_RUN" = "1" ]; then
    say ""
    say "dry run: would download $url"
    exit 0
fi

# ---- download ----
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
file="$tmp/$(basename "$url")"
say ""
say "Downloading ..."
curl -fsSL --proto '=https' --tlsv1.2 -o "$file" "$url" || die "download failed."

# ---- elevate only when needed ----
SUDO=""
if [ "$(id -u)" -ne 0 ] && [ "$kind" != "tar" ]; then
    command -v sudo >/dev/null 2>&1 || die "installing a $kind package needs root; re-run as root."
    SUDO="sudo"
    say "Installing (you may be prompted for your password) ..."
else
    say "Installing ..."
fi

case "$kind" in
    deb) $SUDO apt-get install -y "$file" ;;
    rpm)
        if command -v dnf >/dev/null 2>&1; then $SUDO dnf install -y "$file"
        elif command -v zypper >/dev/null 2>&1; then $SUDO zypper --non-interactive install --allow-unsigned-rpm "$file"
        else $SUDO rpm -Uvh "$file"; fi
        ;;
    tar)
        dir="$tmp/x"
        mkdir -p "$dir"
        tar xzf "$file" -C "$dir"
        inner="$(find "$dir" -maxdepth 2 -name install.sh -print -quit)"
        [ -n "$inner" ] || die "the tarball did not contain install.sh."
        # The bundled installer is a bash script (it uses 'set -o pipefail'), so run it with
        # bash rather than whatever /bin/sh happens to be, which is dash on Debian/Ubuntu.
        command -v bash >/dev/null 2>&1 || die "bash is required for the portable install."
        bash "$inner"
        ;;
esac

say ""
if command -v bz >/dev/null 2>&1; then
    say "Installed: $(bz --version 2>/dev/null || echo BoltZip)"
else
    say "Installed. If 'bz' is not found, open a new shell or add ~/.local/bin to your PATH."
fi
say "Try:  bz --help    |    bz create backup.bz ./my-folder"
