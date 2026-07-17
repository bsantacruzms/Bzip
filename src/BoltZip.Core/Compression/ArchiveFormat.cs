namespace BoltZip.Core.Compression;

/// <summary>All archive formats BoltZip understands, for reading and/or writing.</summary>
public enum ArchiveFormat
{
    Unknown = 0,

    /// <summary>BoltZip's native format: Zstandard content with optional XChaCha20-Poly1305 encryption.</summary>
    Bz,

    Zip,
    SevenZip,
    Tar,
    Gzip,
    Bzip2,
    Zstd,
    Brotli,
    Xz,
    Lzip,
    Rar,
}

/// <summary>A detected format together with whether it is wrapped in a tar container.</summary>
public readonly record struct FormatDescriptor(ArchiveFormat Format, bool TarWrapped)
{
    public bool IsSingleStreamCodec =>
        Format is ArchiveFormat.Gzip
            or ArchiveFormat.Bzip2
            or ArchiveFormat.Zstd
            or ArchiveFormat.Brotli
            or ArchiveFormat.Xz
            or ArchiveFormat.Lzip;
}

/// <summary>Format capability lookup and detection (by extension, then by magic bytes).</summary>
public static class FormatInfo
{
    public static bool CanCreate(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Bz => true,
        ArchiveFormat.Zip => true,
        ArchiveFormat.Tar => true,
        ArchiveFormat.Gzip => true,
        ArchiveFormat.Bzip2 => true,
        ArchiveFormat.Zstd => true,
        ArchiveFormat.Brotli => true,
        _ => false,
    };

    public static bool CanExtract(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Unknown => false,
        _ => true,
    };

    /// <summary>True when the format is a single compressed stream (needs tar for many files).</summary>
    public static bool IsSingleStreamCodec(ArchiveFormat format) =>
        format is ArchiveFormat.Gzip
            or ArchiveFormat.Bzip2
            or ArchiveFormat.Zstd
            or ArchiveFormat.Brotli
            or ArchiveFormat.Xz
            or ArchiveFormat.Lzip;

    /// <summary>Detect the format of a path from its (possibly compound) extension.</summary>
    public static FormatDescriptor DetectFromPath(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();

        if (name.EndsWith(".tar.gz", StringComparison.Ordinal) || name.EndsWith(".tgz", StringComparison.Ordinal))
        {
            return new FormatDescriptor(ArchiveFormat.Gzip, TarWrapped: true);
        }

        if (name.EndsWith(".tar.bz2", StringComparison.Ordinal) ||
            name.EndsWith(".tbz2", StringComparison.Ordinal) ||
            name.EndsWith(".tbz", StringComparison.Ordinal))
        {
            return new FormatDescriptor(ArchiveFormat.Bzip2, TarWrapped: true);
        }

        if (name.EndsWith(".tar.zst", StringComparison.Ordinal) || name.EndsWith(".tzst", StringComparison.Ordinal))
        {
            return new FormatDescriptor(ArchiveFormat.Zstd, TarWrapped: true);
        }

        if (name.EndsWith(".tar.xz", StringComparison.Ordinal) || name.EndsWith(".txz", StringComparison.Ordinal))
        {
            return new FormatDescriptor(ArchiveFormat.Xz, TarWrapped: true);
        }

        if (name.EndsWith(".tar.br", StringComparison.Ordinal))
        {
            return new FormatDescriptor(ArchiveFormat.Brotli, TarWrapped: true);
        }

        if (name.EndsWith(".tar.lz", StringComparison.Ordinal))
        {
            return new FormatDescriptor(ArchiveFormat.Lzip, TarWrapped: true);
        }

        var format = Path.GetExtension(name) switch
        {
            ".bz" => ArchiveFormat.Bz,
            ".zip" => ArchiveFormat.Zip,
            ".7z" => ArchiveFormat.SevenZip,
            ".tar" => ArchiveFormat.Tar,
            ".gz" or ".gzip" => ArchiveFormat.Gzip,
            ".bz2" => ArchiveFormat.Bzip2,
            ".zst" => ArchiveFormat.Zstd,
            ".br" => ArchiveFormat.Brotli,
            ".xz" => ArchiveFormat.Xz,
            ".lz" or ".lzip" => ArchiveFormat.Lzip,
            ".rar" => ArchiveFormat.Rar,
            _ => ArchiveFormat.Unknown,
        };

        return new FormatDescriptor(format, TarWrapped: false);
    }

    /// <summary>The default file extension for a created archive of the given format.</summary>
    public static string DefaultExtension(ArchiveFormat format, bool tarWrapped) => format switch
    {
        ArchiveFormat.Bz => ".bz",
        ArchiveFormat.Zip => ".zip",
        ArchiveFormat.SevenZip => ".7z",
        ArchiveFormat.Tar => ".tar",
        ArchiveFormat.Gzip => tarWrapped ? ".tar.gz" : ".gz",
        ArchiveFormat.Bzip2 => tarWrapped ? ".tar.bz2" : ".bz2",
        ArchiveFormat.Zstd => tarWrapped ? ".tar.zst" : ".zst",
        ArchiveFormat.Brotli => tarWrapped ? ".tar.br" : ".br",
        ArchiveFormat.Xz => tarWrapped ? ".tar.xz" : ".xz",
        ArchiveFormat.Lzip => tarWrapped ? ".tar.lz" : ".lz",
        ArchiveFormat.Rar => ".rar",
        _ => ".bin",
    };

    /// <summary>Sniff a format from the leading magic bytes of an archive stream.</summary>
    public static ArchiveFormat SniffFromMagic(ReadOnlySpan<byte> header)
    {
        static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
            data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

        if (StartsWith(header, "BZ1"u8))
        {
            return ArchiveFormat.Bz;
        }

        if (StartsWith(header, "PK\x03\x04"u8) || StartsWith(header, "PK\x05\x06"u8))
        {
            return ArchiveFormat.Zip;
        }

        if (StartsWith(header, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))
        {
            return ArchiveFormat.SevenZip;
        }

        if (StartsWith(header, "Rar!"u8))
        {
            return ArchiveFormat.Rar;
        }

        if (StartsWith(header, new byte[] { 0x1F, 0x8B }))
        {
            return ArchiveFormat.Gzip;
        }

        if (StartsWith(header, "BZh"u8))
        {
            return ArchiveFormat.Bzip2;
        }

        if (StartsWith(header, new byte[] { 0x28, 0xB5, 0x2F, 0xFD }))
        {
            return ArchiveFormat.Zstd;
        }

        if (StartsWith(header, new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }))
        {
            return ArchiveFormat.Xz;
        }

        if (StartsWith(header, "LZIP"u8))
        {
            return ArchiveFormat.Lzip;
        }

        return ArchiveFormat.Unknown;
    }
}
