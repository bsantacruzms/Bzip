# ⚡ BoltZip

**A modern, open-source archiver — a spiritual successor to 7‑Zip, rebuilt on .NET 8 with
automatic hardware optimization and a fast, authenticated‑encryption native format.**

BoltZip looks at *your* machine — CPU cores, RAM, storage type (NVMe/SSD/HDD/network), AES and
SIMD support — and auto‑tunes every compression job. No other mainstream archiver does this.

📊 **[Live showcase &amp; benchmarks →](https://bsantacruzms.github.io/Bzip/)**

---

## Why BoltZip is different

- 🧠 **Automatic hardware optimization** — threads, match‑window size, I/O buffers, long‑distance
  matching and codec level are chosen for your exact hardware, per job. And it **tells you why**.
- 🔒 **Modern authenticated encryption** — the native `.bz` format uses **XChaCha20‑Poly1305**
  (AEAD) with **Argon2id** key derivation. Tampering and wrong passwords fail loudly instead of
  silently producing garbage.
- ⚡ **Fast modern codec** — `.bz` is built on **Zstandard**, which beats classic Deflate/LZMA on
  the speed‑vs‑ratio curve and scales across cores.
- 🆓 **Free, open source, no ads, no nag screens.**
- 📦 **Portable** — single‑file executables, nothing to install.
- 🖥️ **Three ways to use it** — a modern desktop app, a `bz` CLI, and Windows right‑click.
- 🌍 **Cross‑platform core** — Windows today; macOS and Linux in progress.

## Comparison

| | ⚡ **BoltZip** | 7‑Zip | WinRAR | WinZip | Windows ZIP | macOS Archive Utility |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| **Price** | Free | Free | Paid (trial) | Paid (subscription) | Free (built‑in) | Free (built‑in) |
| **Open source** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Auto hardware optimization** | ✅ **unique** | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Explains its tuning choices** | ✅ **unique** | ❌ | ❌ | ❌ | ❌ | ❌ |
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
(single-threaded Deflate) — BoltZip's edge is its multi-core native format, not every workload.
Reproduce it with `pwsh scripts/benchmark.ps1`. (7-Zip 18.01; WinRAR/WinZip weren't installed, so
they're in the feature table above but not timed.)

## Install

**Windows** — download from the [Releases](https://github.com/bsantacruzms/Bzip/releases) page:

- **`BoltZip-<version>.msi`** — installer. Adds the app + `bz` CLI, a Start‑menu entry, and a
  cascading **“BoltZip”** right‑click menu (Add to archive / Extract) in Explorer. The setup wizard
  also has a checkbox (on by default) to **associate archives** — `.bz`, `.zip`, `.7z`, `.rar`,
  `.tar`, `.gz`, `.zst`, `.xz` and more — so double‑clicking one opens it in BoltZip.
- **`BoltZipTool-<version>-portable.exe`** — the desktop app, no install (no system changes).
- **`bz-<version>-portable.exe`** — the CLI.

**macOS** — `BoltZip-<version>-<arch>.dmg` (drag to Applications). The app declares the archive types
it can open, so they list BoltZip under **Finder → Open With** (pick *Change All…* to make it the default).

**Linux** — install the package for your distro, or use the portable tarball:

```bash
# Debian / Ubuntu
sudo apt install ./boltzip_<version>_amd64.deb
# Fedora / RHEL / openSUSE
sudo dnf install ./boltzip-<version>-1.x86_64.rpm
# Portable (any distro): extract, then link the file types
tar xzf BoltZip-<version>-linux-x64.tar.gz && cd BoltZip-<version>-linux-x64 && ./install.sh
```

The `.deb`/`.rpm` register the `.bz` MIME type and add BoltZip to the file manager’s “Open with”.
Right‑click menus and file associations are added by the installers only; the portable builds never
touch your system unless you run `install.sh`.

## CLI usage

```text
bz create <output> <input...> [--goal fast|balanced|max] [-p [password]] [-q]
bz extract <archive> [--out <dir>] [-y] [-p [password]] [-q]
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
```

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

Free and open source under the [MIT license](LICENSE) — use it, study it, and build on it.

---

Created by Brian Santacruz — [briansantacruz.com](https://briansantacruz.com)
