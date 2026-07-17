# ⚡ BoltZip

A modern, open-source archiver for Windows — a spiritual successor to 7‑Zip, rebuilt on
.NET 8 with **automatic hardware optimization** and a fast, encrypted native format.

> Working name — the product and its `.bz` format can be renamed freely.

## Highlights

- **Auto hardware optimization.** BoltZip profiles your CPU, RAM and storage (NVMe/SSD/HDD/
  network) and auto‑tunes thread count, match‑window size, I/O buffers, long‑distance matching
  and codec level for every job. Run `bz hw` (or open the app) to see exactly what it chose and why.
- **New native `.bz` format.** A solid [Zstandard](https://facebook.github.io/zstd/) stream with a
  separate compressed index, optionally protected end‑to‑end with **XChaCha20‑Poly1305**
  authenticated encryption and **Argon2id** password hashing.
- **Broad format support.**
  - Create: `.bz`, `.zip`, `.tar`, `.gz`, `.bz2`, `.zst`, `.br`
  - Extract: all of the above plus `.7z`, `.rar`, `.xz`, `.lz`, `.arj`
- **Three interfaces:** a dark‑themed WPF app, a `bz` command‑line tool, and Windows
  right‑click ("Add to BoltZip archive" / "Extract with BoltZip").
- **Portable & self‑contained.** Single‑file executables, no install required.

## Why a new format?

Legacy archivers still default to decades‑old codecs. `.bz` pairs Zstandard (excellent ratio at
high speed, great multi‑core scaling) with modern authenticated encryption:

| Concern | BoltZip `.bz` |
| --- | --- |
| Compression | Zstandard, solid, auto‑tuned window/level |
| Encryption | XChaCha20‑Poly1305 (AEAD) |
| Key derivation | Argon2id (memory‑hard) |
| Integrity | Per‑chunk authentication tags; reorder/truncation resistant |
| Key separation | HKDF‑SHA256 sub‑keys for index and content |

Encryption authenticates every chunk, so tampering or a wrong password fails loudly instead of
producing garbage.

## Install

Download the portable executables from the
[Releases](https://github.com/bsantacruzms/boltzip/releases) page:

- `BoltZipTool-<version>-portable.exe` — the GUI
- `bz-<version>-portable.exe` — the CLI

No installation needed. To add the right‑click menu, open the app and click **Add right‑click menu**,
or run `bz install-context`.

## CLI usage

```text
bz create <output> <input...> [--goal fast|balanced|max] [-p [password]] [-q]
bz extract <archive> [--out <dir>] [-y] [-p [password]] [-q]
bz list <archive> [-p [password]]
bz detect <file>
bz hw
bz install-context [--app <BoltZipTool.exe>]
bz uninstall-context
```

Examples:

```powershell
# Encrypted native archive, auto-tuned to this machine
bz create backup.bz .\Documents -p

# Maximum ratio zip
bz create photos.zip .\Photos --goal max

# Extract anywhere
bz extract backup.bz --out .\restored -y

# Preview the hardware plan
bz hw
```

## Building from source

Requires the .NET 8 SDK.

```powershell
dotnet build BoltZip.sln -c Release
dotnet test tests/BoltZip.Core.Tests/BoltZip.Core.Tests.csproj

# Portable single-file executables into dist/
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

## Project layout

| Project | Description |
| --- | --- |
| `src/BoltZip.Core` | Engine: hardware probe, optimization planner, `.bz` format, all codecs |
| `src/BoltZip.Cli` | `bz` command-line tool |
| `src/BoltZip.App` | WPF desktop app (dark theme) |
| `tests/BoltZip.Core.Tests` | xUnit tests (round-trips, encryption, planner) |

## Security notes

- `.bz` encryption uses libsodium's XChaCha20‑Poly1305 with Argon2id key derivation.
- Extraction guards against path traversal ("Zip Slip").
- Passwords are never logged and are zeroed after key derivation.

## License

Open source. See `LICENSE`.

---

Created by Brian Santacruz — [briansantacruz.com](https://briansantacruz.com)
