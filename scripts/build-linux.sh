#!/usr/bin/env bash
#
# Builds Linux packages for BoltZip: a portable .tar.gz plus native .deb and .rpm.
# Runs on Linux only (uses dpkg/rpmbuild via fpm) - see .github/workflows/release.yml.
#
# Requirements on the build host:
#   - .NET 8 SDK
#   - fpm            (gem install fpm)          -> builds .deb and .rpm
#   - rpm/rpmbuild   (apt-get install rpm)      -> needed by fpm for .rpm
# If fpm is not present, only the portable .tar.gz is produced.
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

VERSION="$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props | head -n1 || true)"
VERSION="${VERSION:-1.0.3}"
MAINTAINER="BoltZip"
URL="https://github.com/bsantacruzms/Bzip"
DESC="Modern, hardware-optimized archiver with a fast authenticated-encryption format."
ICON="src/BoltZip.App/Assets/boltzip.png"

echo "BoltZip $VERSION - building Linux packages"
mkdir -p dist

HAVE_FPM=0
if command -v fpm >/dev/null 2>&1; then
    HAVE_FPM=1
else
    echo "!! fpm not found - will build the portable tarball only (install: gem install fpm)"
fi

build_one() {
    local rid="$1" debarch="$2" rpmarch="$3" tararch="$4"
    echo "==> Publishing $rid"

    rm -rf "publish/gui-$rid" "publish/cli-$rid"
    dotnet publish src/BoltZip.App/BoltZip.App.csproj -c Release -r "$rid" \
        --self-contained true -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true -p:DebugType=none \
        -o "publish/gui-$rid"
    dotnet publish src/BoltZip.Cli/BoltZip.Cli.csproj -c Release -r "$rid" \
        --self-contained true -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true -p:DebugType=none \
        -o "publish/cli-$rid"

    local gui="publish/gui-$rid/BoltZipTool"
    local cli="publish/cli-$rid/bz"
    chmod +x "$gui" "$cli"

    # ---- FHS staged tree used for both .deb and .rpm ----
    local stage="publish/pkg-$rid"
    rm -rf "$stage"
    install -Dm 755 "$gui" "$stage/usr/lib/boltzip/BoltZipTool"
    install -Dm 755 "$cli" "$stage/usr/lib/boltzip/bz"
    mkdir -p "$stage/usr/bin"
    ln -sf ../lib/boltzip/BoltZipTool "$stage/usr/bin/boltzip"
    ln -sf ../lib/boltzip/bz "$stage/usr/bin/bz"
    install -Dm 644 installers/linux/boltzip.desktop "$stage/usr/share/applications/boltzip.desktop"
    install -Dm 644 "$ICON" "$stage/usr/share/icons/hicolor/256x256/apps/boltzip.png"
    install -Dm 644 installers/linux/boltzip-mime.xml "$stage/usr/share/mime/packages/boltzip.xml"

    # ---- Portable, relocatable tarball (run ./install.sh to link file types) ----
    local tname="BoltZip-$VERSION-linux-$tararch"
    local tdir="publish/$tname"
    rm -rf "$tdir"; mkdir -p "$tdir"
    cp "$gui" "$tdir/BoltZipTool"
    cp "$cli" "$tdir/bz"
    cp "$ICON" "$tdir/boltzip.png"
    cp installers/linux/boltzip.desktop "$tdir/boltzip.desktop"
    cp installers/linux/boltzip-mime.xml "$tdir/boltzip-mime.xml"
    cp installers/linux/install.sh "$tdir/install.sh"
    chmod +x "$tdir/BoltZipTool" "$tdir/bz" "$tdir/install.sh"
    cat > "$tdir/README.txt" <<EOF
BoltZip $VERSION (portable, linux-$tararch)

Contents:
  BoltZipTool   the desktop app
  bz            the command-line tool
  install.sh    installs both and links archive files to open with BoltZip

Quick start (no install):
  ./bz --help
  ./BoltZipTool

Install and link file types:
  ./install.sh          (per-user, into ~/.local)
  sudo ./install.sh     (system-wide, into /usr/local)
EOF
    tar -czf "dist/$tname.tar.gz" -C publish "$tname"
    echo "==> dist/$tname.tar.gz"

    # ---- Native .deb and .rpm (via fpm) ----
    if [ "$HAVE_FPM" -eq 1 ]; then
        rm -f "dist/boltzip_${VERSION}_${debarch}.deb" "dist/boltzip-${VERSION}-1.${rpmarch}.rpm"
        fpm -s dir -t deb -n boltzip -v "$VERSION" -a "$debarch" \
            --description "$DESC" --url "$URL" --maintainer "$MAINTAINER" \
            --license "MIT" --vendor "BoltZip" --category utils \
            --deb-recommends libfontconfig1 --deb-recommends libx11-6 \
            --after-install installers/linux/postinstall.sh \
            --after-remove installers/linux/postremove.sh \
            -C "$stage" -p "dist/boltzip_${VERSION}_${debarch}.deb" usr
        fpm -s dir -t rpm -n boltzip -v "$VERSION" -a "$rpmarch" \
            --description "$DESC" --url "$URL" --maintainer "$MAINTAINER" \
            --license "MIT" --vendor "BoltZip" --category "Applications/Archiving" \
            --after-install installers/linux/postinstall.sh \
            --after-remove installers/linux/postremove.sh \
            -C "$stage" -p "dist/boltzip-${VERSION}-1.${rpmarch}.rpm" usr
        echo "==> dist/boltzip_${VERSION}_${debarch}.deb"
        echo "==> dist/boltzip-${VERSION}-1.${rpmarch}.rpm"
    fi
}

build_one linux-x64   amd64 x86_64  x64
build_one linux-arm64 arm64 aarch64 arm64

echo
echo "Done. Linux artifacts:"
ls -1 dist | grep -E '\.(tar\.gz|deb|rpm)$' || true
