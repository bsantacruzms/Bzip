#!/usr/bin/env bash
#
# Builds a signed APT repository from BoltZip .deb packages, so users can install with:
#
#   sudo apt install boltzip
#
# after adding the repository once. This is the same model Ookla's speedtest, Docker and
# Microsoft use: a third-party APT repo, not the distro's own archive.
#
# Usage:
#   scripts/build-apt-repo.sh --debs dist --out apt-repo [--suite stable] [--origin BoltZip]
#
# Signing: if GPG_KEY_ID is set (and the secret key is in the keyring) the Release file is
# signed, producing InRelease + Release.gpg and exporting the public key. Without it the repo
# is built unsigned, which apt will refuse by default, so signing is expected for production.
#
# Requirements: apt-utils (apt-ftparchive), dpkg (dpkg-deb), gnupg for signing.
#
set -euo pipefail

DEB_DIR="dist"
OUT_DIR="apt-repo"
SUITE="stable"
COMPONENT="main"
ORIGIN="BoltZip"
LABEL="BoltZip"
DESCRIPTION="BoltZip, modern hardware-optimized archiver"
REPO_URL="https://bsantacruzms.github.io/boltzip-apt"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --debs) DEB_DIR="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        --suite) SUITE="$2"; shift 2 ;;
        --origin) ORIGIN="$2"; shift 2 ;;
        --url) REPO_URL="$2"; shift 2 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

for tool in apt-ftparchive dpkg-deb; do
    command -v "$tool" >/dev/null 2>&1 || {
        echo "Missing '$tool'. Install with: sudo apt-get install -y apt-utils dpkg" >&2
        exit 1
    }
done

shopt -s nullglob
debs=("$DEB_DIR"/*.deb)
if [[ ${#debs[@]} -eq 0 ]]; then
    echo "No .deb files found in '$DEB_DIR'." >&2
    exit 1
fi

echo "Building APT repository from ${#debs[@]} package(s)"
rm -rf "$OUT_DIR"
POOL="$OUT_DIR/pool/$COMPONENT/b/boltzip"
mkdir -p "$POOL"
cp "${debs[@]}" "$POOL/"

# Collect the architectures actually present so we only advertise what we ship.
architectures=()
for deb in "$POOL"/*.deb; do
    arch="$(dpkg-deb --field "$deb" Architecture)"
    [[ " ${architectures[*]-} " == *" $arch "* ]] || architectures+=("$arch")
done
echo "Architectures: ${architectures[*]}"

# One Packages listing for the whole pool, then split per architecture. Paths inside it must
# be relative to the repository root, so apt-ftparchive runs from there.
all_packages="$(mktemp)"
(cd "$OUT_DIR" && apt-ftparchive packages "pool/$COMPONENT") > "$all_packages"

for arch in "${architectures[@]}"; do
    dir="$OUT_DIR/dists/$SUITE/$COMPONENT/binary-$arch"
    mkdir -p "$dir"
    awk -v arch="$arch" 'BEGIN { RS = ""; ORS = "\n\n" } $0 ~ ("(^|\n)Architecture: " arch "(\n|$)") { print }' \
        "$all_packages" > "$dir/Packages"
    gzip -9kf "$dir/Packages"
    echo "  $arch: $(grep -c '^Package:' "$dir/Packages") package(s)"
done
rm -f "$all_packages"

# ---- Release file ----
release_conf="$(mktemp)"
cat > "$release_conf" <<EOF
APT::FTPArchive::Release::Origin "$ORIGIN";
APT::FTPArchive::Release::Label "$LABEL";
APT::FTPArchive::Release::Suite "$SUITE";
APT::FTPArchive::Release::Codename "$SUITE";
APT::FTPArchive::Release::Architectures "${architectures[*]}";
APT::FTPArchive::Release::Components "$COMPONENT";
APT::FTPArchive::Release::Description "$DESCRIPTION";
EOF

apt-ftparchive -c "$release_conf" release "$OUT_DIR/dists/$SUITE" > "$OUT_DIR/dists/$SUITE/Release"
rm -f "$release_conf"

# ---- Signing ----
if [[ -n "${GPG_KEY_ID:-}" ]]; then
    echo "Signing Release with key $GPG_KEY_ID"
    gpg_opts=(--batch --yes --default-key "$GPG_KEY_ID")
    if [[ -n "${GPG_PASSPHRASE:-}" ]]; then
        gpg_opts+=(--pinentry-mode loopback --passphrase "$GPG_PASSPHRASE")
    fi

    gpg "${gpg_opts[@]}" --armor --detach-sign \
        --output "$OUT_DIR/dists/$SUITE/Release.gpg" "$OUT_DIR/dists/$SUITE/Release"
    gpg "${gpg_opts[@]}" --clearsign \
        --output "$OUT_DIR/dists/$SUITE/InRelease" "$OUT_DIR/dists/$SUITE/Release"

    # Public key in the binary format apt expects under /usr/share/keyrings.
    gpg --batch --yes --export "$GPG_KEY_ID" > "$OUT_DIR/boltzip-archive-keyring.gpg"
    echo "Wrote $OUT_DIR/boltzip-archive-keyring.gpg"
else
    echo "!! GPG_KEY_ID not set: repository is UNSIGNED."
    echo "   apt will reject it unless clients use [trusted=yes], which disables authenticity"
    echo "   checks. Set GPG_KEY_ID (and GPG_PASSPHRASE) to publish a signed repository."
fi

cat > "$OUT_DIR/index.html" <<EOF
<!doctype html>
<meta charset="utf-8">
<title>BoltZip APT repository</title>
<h1>BoltZip APT repository</h1>
<p>Install BoltZip on Debian, Ubuntu and derivatives:</p>
<pre>
curl -fsSL $REPO_URL/boltzip-archive-keyring.gpg | sudo tee /usr/share/keyrings/boltzip-archive-keyring.gpg > /dev/null
echo "deb [signed-by=/usr/share/keyrings/boltzip-archive-keyring.gpg] $REPO_URL $SUITE $COMPONENT" | sudo tee /etc/apt/sources.list.d/boltzip.list
sudo apt update
sudo apt install boltzip
</pre>
EOF

echo
echo "APT repository ready in '$OUT_DIR'."
echo "Publish its contents at the URL clients will point at."
