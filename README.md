# ⚡ BoltZip

**A modern, open-source archiver — a spiritual successor to 7‑Zip, rebuilt on .NET 8 with
automatic hardware optimization and a fast, authenticated‑encryption native format.**

BoltZip looks at *your* machine — CPU cores, RAM, storage type (NVMe/SSD/HDD/network), AES and
SIMD support — and auto‑tunes every compression job. No other mainstream archiver does this.

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

## Install

Download the portable executables from the
[Releases](https://github.com/bsantacruzms/Bzip/releases) page:

- `BoltZipTool-<version>-portable.exe` — the desktop app
- `bz-<version>-portable.exe` — the CLI

Nothing to install. To add the right‑click menu, open the app and click **Add right‑click menu**,
or run `bz install-context`.

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

## License

Open source. See `LICENSE`.

---

Created by Brian Santacruz — [briansantacruz.com](https://briansantacruz.com)
