namespace BoltZip.Core.Bz;

/// <summary>
/// On-disk constants and layout for BoltZip's native <c>.bz</c> container.
/// </summary>
/// <remarks>
/// Layout (little-endian):
/// <code>
/// magic "BZ1" (3) | version (1) | flags (1) | codec (1) | reserved (2)
/// [if encrypted] salt (16) | noncePrefix (16) | opsLimit (i64) | memLimit (i32) | chunkSize (i32)
/// indexPlainLen (i32) | indexStoredLen (i32) | index bytes
/// contentStoredLen (i64) | content bytes
/// </code>
/// The index is a zstd-compressed blob of entry metadata (optionally chunk-AEAD encrypted).
/// The content is a single solid zstd stream of concatenated file bytes (optionally
/// chunk-AEAD encrypted with XChaCha20-Poly1305).
/// </remarks>
internal static class BzFormat
{
    public static ReadOnlySpan<byte> Magic => "BZ1"u8;

    public const byte Version = 1;
    public const byte CodecZstd = 1;

    [Flags]
    public enum HeaderFlags : byte
    {
        None = 0,
        Encrypted = 1,
    }

    // Encryption parameters.
    public const int SaltBytes = 16;         // crypto_pwhash_argon2id_SALTBYTES
    public const int NoncePrefixBytes = 16;  // 16 random + 8 counter = 24-byte XChaCha20 nonce
    public const int KeyBytes = 32;
    public const int DefaultChunkSize = 1 << 20; // 1 MiB authenticated chunks

    // Argon2id KDF work factors (libsodium "Moderate": 128 MiB, 6 passes).
    public const long ArgonOpsLimit = 6;
    public const int ArgonMemLimit = 134_217_728;

    // Key-derivation domain separators for HKDF sub-keys.
    public const string ContentKeyInfo = "boltzip-bz-content-v1";
    public const string IndexKeyInfo = "boltzip-bz-index-v1";
}
