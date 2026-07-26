# ⚡ BoltZip

**A modern, open-source archiver, a spiritual successor to 7‑Zip, rebuilt on .NET 8 with
automatic hardware optimization and a fast, authenticated‑encryption native format.**

BoltZip looks at *your* machine, CPU cores, RAM, storage type (NVMe/SSD/HDD/network), AES and
SIMD support, and auto‑tunes every compression job. No other mainstream archiver does this.

📊 **[Live showcase &amp; benchmarks →](https://bsantacruzms.github.io/Bzip/)**

---

## Why BoltZip is different

- 🧠 **Automatic hardware optimization**, threads, match‑window size, I/O buffers, long‑distance
  matching and codec level are chosen for your exact hardware, per job. And it **tells you why**.
- 🔒 **Modern authenticated encryption**, the native `.bz` format uses **XChaCha20‑Poly1305**
  (AEAD) with **Argon2id** key derivation. Tampering and wrong passwords fail loudly instead of
  silently producing garbage.
- ⚡ **Fast modern codec**, `.bz` is built on **Zstandard**, which beats classic Deflate/LZMA on
  the speed‑vs‑ratio curve and scales across cores.
- � **Smart with media**, already‑compressed files (video, photos, music, existing archives) can't
  be shrunk further by any lossless tool, so when **archiving** BoltZip detects them and stores them
  at full speed instead of wasting CPU, bit‑for‑bit identical and packed fast.
- 🎞️ **Shrink videos on your GPU**, and when you actually want a video *smaller*, `bz video`
  re‑encodes it with your graphics hardware (NVIDIA NVENC, AMD AMF, Intel Quick Sync, Apple
  VideoToolbox on macOS) at a visually‑lossless setting, typically **50–60% smaller in seconds**.
  No other archiver does this.
- �🆓 **Free, open source, no ads, no nag screens.**
- 📦 **Portable**, single‑file executables, nothing to install.
- 🖥️ **Three ways to use it**, a modern desktop app, a `bz` CLI, and Windows right‑click.
- 🌍 **Cross‑platform core**, Windows today; macOS and Linux in progress.

## Comparison

| | ⚡ **BoltZip** | 7‑Zip | WinRAR | WinZip | Windows ZIP | macOS Archive Utility |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| **Price** | Free | Free | Paid (trial) | Paid (subscription) | Free (built‑in) | Free (built‑in) |
| **Open source** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Auto hardware optimization** | ✅ **unique** | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Explains its tuning choices** | ✅ **unique** | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Auto media‑aware fast path** | ✅ **unique** | ⚠️ | ❌ | ❌ | ❌ | ❌ |
| **Shrink video (GPU re‑encode)** | ✅ **unique** | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Modern codec (Zstandard)** | ✅ native | ⚠️ fork only | ❌ | ⚠️ newer versions | ❌ | ❌ |
| **Authenticated encryption (AEAD)** | ✅ XChaCha20‑Poly1305 | ❌ AES‑256 (no auth) | ❌ AES‑256 | ❌ AES‑256 | ❌ ZipCrypto (weak) | ❌ |
| **Memory‑hard password KDF (Argon2id)** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Tamper detection on encrypted data** | ✅ per‑chunk | ⚠️ CRC only | ⚠️ checksums | ⚠️ | ❌ | ❌ |
| **Multi‑core compression** | ✅ auto | ✅ manual | ✅ | ✅ | ❌ | ❌ |
| **Hardware‑AES aware** | ✅ | ⚠️ | ⚠️ | ⚠️ | – | – |
| **Portable (no install)** | ✅ single‑file | ✅ | ❌ | ❌ | – | – |
| **CLI included** | ✅ `bz` | ✅ | ✅ | ⚠️ add‑on | ⚠️ `tar`/PowerShell | ⚠️ `zip`/`ditto` |
| **Right‑click integration** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Ad‑free / no nag** | ✅ | ✅ | ❌ | ❌ | ✅ | ✅ |
| **Cross‑platform (Win/mac/Linux)** | ✅¹ | ✅ | ✅ | ⚠️ Win/mac | ❌ | ❌ |
| **Create** | `.bz` `.zip` `.tar` `.gz` `.bz2` `.zst` `.br` | `7z` `zip` `tar` `gz` `bz2` `xz` `wim` | `rar` `zip` | `zip` `zipx` | `zip` | `zip` |
| **Extract** | `.bz` `zip` `7z` `rar` `tar` `gz` `bz2` `xz` `lz` `zst` `br` `arj` | many | many | many | `zip` (Win 11: +`tar`/`7z`/`rar`) | `zip` `tar` `gz` `bz2` `xz` |

<sub>✅ yes · ⚠️ partial/limited · ❌ no · – not applicable. ¹ Windows now; macOS & Linux in progress.
Comparison reflects the products' default/typical configurations and is provided in good faith.</sub>

### What that means in practice

- **7‑Zip** is excellent and free, but its encryption is unauthenticated AES‑256 with a legacy
  key‑derivation, it never auto‑tunes to your hardware, and it can't tell you *why* it chose a setting.
- **WinRAR / WinZip** are paid, closed‑source, and show nag/upsell screens. Neither optimizes to
  your machine.
- **Windows built‑in ZIP** and **macOS Archive Utility** are convenient but limited: old codecs,
  weak or no encryption, no tuning, no modern format.
- **BoltZip** is the only one that profiles your hardware, uses modern authenticated encryption by
  default in its native format, and is free, open, portable and scriptable.

## Benchmarks

Real numbers on a 100 MB mixed dataset (~50% text, 25% CSV, 25% incompressible), Intel Core Ultra 9
285K (24 cores). Lower is better.

| Tool | Format | Compress | Extract | Size |
| --- | --- | ---: | ---: | ---: |
| **BoltZip** | `.bz` | **2.80 s** | 3.64 s | **25.8 MB** |
| BoltZip (fast) | `.bz` | 2.71 s | 3.63 s | 26.2 MB |
| **BoltZip** | `.zip` | 3.23 s | 0.26 s | 29.1 MB |
| 7‑Zip | `.7z` | 5.82 s | 0.15 s | 25.9 MB |
| 7‑Zip | `.zip` | 1.08 s | 0.12 s | 28.4 MB |
| Windows Zip | `.zip` | **1.05 s** | 0.12 s | 29.0 MB |

Medians of three warmed runs. BoltZip's `.bz` now compresses on **all CPU cores** (independent
Zstandard frames), so in the native-format comparison it was **~2× faster than 7-Zip's `.7z`** and
slightly smaller on this dataset. For plain `.zip` creation, 7-Zip and Windows are still fastest
(single-threaded Deflate), BoltZip's edge is its multi-core native format, not every workload.
Reproduce it with `pwsh scripts/benchmark.ps1`. (7-Zip 18.01; WinRAR/WinZip weren't installed, so
they're in the feature table above but not timed.)

### Video & media files

Already-compressed media (video, photos, music) can't be shrunk by any lossless archiver, BoltZip,
7-Zip or WinRAR alike, so every tool lands at ~100% of the original size. The difference is **time**:
BoltZip detects media and stores it at full speed across all cores instead of trying to compress the
incompressible. On a 500 MB media set:

| Tool | Format | Compress | Size | Ratio |
| --- | --- | ---: | ---: | ---: |
| **BoltZip** | `.bz` | **2.93 s** | 500 MB | 100% |
| 7-Zip | `.7z` | 6.96 s | 500 MB | 100% |
| 7-Zip | `.zip` | 5.12 s | 500 MB | 100% |
| Windows Zip | `.zip` | 10.53 s | 500 MB | 100% |

BoltZip packed the same bytes **~1.7× faster than 7-Zip's `.zip`** and **~2.4× faster than `.7z`**,
with output identical in size (there is nothing to gain). Reproduce with `pwsh scripts/benchmark-media.ps1`.
To make videos genuinely smaller for faster transfer you would need lossy re-encoding (H.265/AV1),
which changes quality; BoltZip keeps files bit-for-bit identical.

## Install

**Windows**, download from the [Releases](https://github.com/bsantacruzms/Bzip/releases) page:

- **`BoltZip-<version>-setup.exe`**, the recommended installer (carries the BoltZip icon). Adds the
  app + `bz` CLI, a Start‑menu entry, a cascading **“BoltZip”** right‑click menu (Add to archive /
  Extract) in Explorer, and file associations so double‑clicking `.bz`, `.zip`, `.7z`, `.rar`,
  `.tar`, `.gz`, `.zst`, `.xz` and more opens them in BoltZip.
- **`BoltZip-<version>.msi`**, the same installer as a raw MSI (its wizard lets you opt out of the
  file associations).
- **`BoltZipTool-<version>-portable.exe`**, the desktop app, no install (no system changes).
- **`bz-<version>-portable.exe`**, the CLI.

**macOS**, `BoltZip-<version>-<arch>.dmg` (drag to Applications). The app declares the archive types
it can open, so they list BoltZip under **Finder → Open With** (pick *Change All…* to make it the default).

**Linux**, one command installs the right package for your system:

```bash
curl -fsSL https://bsantacruzms.github.io/Bzip/install.sh | sh
```

It picks the `.deb`, `.rpm` or portable tarball automatically. (Review it first if you prefer:
`curl -fsSL https://bsantacruzms.github.io/Bzip/install.sh | less`.)

Or install a package by hand, or use the portable tarball:

```bash
# Debian / Ubuntu
sudo apt install ./boltzip_<version>_amd64.deb
# Fedora / RHEL / openSUSE
sudo dnf install ./boltzip-<version>-1.x86_64.rpm
# Portable (any distro): extract, then link the file types
tar xzf BoltZip-<version>-linux-x64.tar.gz && cd BoltZip-<version>-linux-x64 && ./install.sh
```

<sub>BoltZip is not (yet) in Debian's or Ubuntu's own archives, so plain `apt install boltzip`
only works after adding the BoltZip repository. Getting into the official archives requires a
Debian maintainer sponsor and packaging built from source against the distro's .NET runtime,
rather than the self-contained binaries released here.</sub>

The `.deb`/`.rpm` register the `.bz` MIME type and add BoltZip to the file manager’s “Open with”.
Right‑click menus and file associations are added by the installers only; the portable builds never
touch your system unless you run `install.sh`.

## CLI usage

```text
bz create <output> <input...> [--goal fast|balanced|max] [-p [password]] [-q]
bz extract <archive> [--out <dir>] [-y] [-p [password]] [-q]
bz video <file-or-folder> [--out <dir>] [--quality visually-lossless|balanced|smaller] [--codec auto|h265|av1|h264] [--cpu]
bz list <archive> [-p [password]]
bz detect <file>
bz hw                       # show your hardware and the plan it would use
bz install-context / uninstall-context
```

```powershell
# Encrypted native archive, auto‑tuned to this machine
bz create backup.bz .\Documents -p

# Smallest possible zip
bz create photos.zip .\Photos --goal max

# Extract anywhere
bz extract backup.bz --out .\restored -y

# Shrink a video (or a whole folder of them) on your GPU, visually lossless
bz video .\clips\holiday.mp4
```

Re‑encoding a video is lossy by nature, but the default `visually-lossless` setting is
imperceptible; it needs [FFmpeg](https://ffmpeg.org/download.html) on your `PATH`
(`winget install Gyan.FFmpeg`).

## Building from source

Requires the .NET 8 SDK.

```powershell
dotnet build BoltZip.sln -c Release
dotnet test tests/BoltZip.Core.Tests/BoltZip.Core.Tests.csproj

# Portable single‑file executables into dist/
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

On macOS and Linux, run `bash scripts/build-macos.sh` (produces the `.dmg`) or
`bash scripts/build-linux.sh` (produces the `.deb`, `.rpm` and portable tarball).

## Project layout

| Project | Description |
| --- | --- |
| `src/BoltZip.Core` | Engine: hardware probe, optimization planner, `.bz` format, all codecs |
| `src/BoltZip.Cli` | `bz` command‑line tool |
| `src/BoltZip.App` | Desktop app |
| `tests/BoltZip.Core.Tests` | xUnit tests (round‑trips, encryption, planner) |

## Security notes

- `.bz` encryption uses libsodium's XChaCha20‑Poly1305 with Argon2id key derivation.
- Extraction guards against path traversal ("Zip Slip").
- Passwords are never logged and are zeroed after key derivation.
- The whole engine is memory-safe managed .NET, avoiding the native buffer-overflow/underflow bugs
  that have caused remote-code-execution CVEs in C/C++ archivers (e.g. 7-Zip's Zstandard decoder,
  CVE-2024-11477).

## Contributing

BoltZip is free and open source, and contributions are welcome. Found a bug or have an
idea? [Open an issue](https://github.com/bsantacruzms/Bzip/issues) or send a pull request.

If you'd like to support development, tips are appreciated:

- **XRP:** `r4FaiziXJCbh2asirLkRpkGjLB47uHWNpE`
- **XLM:** `GCTCVG44ZOJRYJXTFF7BA23ATPC47H3YOX22WB7X2AKBL3AZ35NR5KJY`

## License

Free and open source under the [MIT license](LICENSE), use it, study it, and build on it.

---

BoltZip is free and open source. [View it on GitHub](https://github.com/bsantacruzms/Bzip).
