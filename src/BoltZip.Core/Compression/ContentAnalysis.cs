namespace BoltZip.Core.Compression;

/// <summary>
/// Inspects a set of input paths to estimate how compressible they are. Files that are
/// already compressed by their own codecs — video, photos, music, and existing archives —
/// gain essentially nothing from a general-purpose compressor, so the planner can store
/// them at maximum speed instead of burning CPU for a fraction of a percent.
/// </summary>
public static class ContentAnalysis
{
    /// <summary>
    /// Extensions whose contents are already compressed by a domain-specific codec. A
    /// lossless archiver (Zstandard, LZMA, Deflate) typically shaves only ~0–2% off these,
    /// so re-compressing them is wasted time, not saved space. Uncompressed media such as
    /// WAV, BMP and TIFF is deliberately excluded — those genuinely do compress.
    /// </summary>
    public static readonly IReadOnlySet<string> AlreadyCompressedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Video (encoded)
            ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm",
            ".mpg", ".mpeg", ".mpe", ".m2v", ".mts", ".m2ts", ".ts", ".3gp",
            ".3g2", ".ogv", ".vob", ".rm", ".rmvb", ".asf", ".divx", ".f4v",
            // Images (already compressed / lossy)
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".webp",
            ".heic", ".heif", ".avif", ".jp2", ".j2k", ".jpx", ".jxl", ".bpg",
            // Audio (encoded)
            ".mp3", ".aac", ".m4a", ".m4b", ".ogg", ".oga", ".opus", ".flac",
            ".wma", ".ac3", ".eac3", ".dts", ".ape", ".wv", ".mka", ".amr",
            // Already-compressed containers / packages
            ".zip", ".7z", ".rar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz",
            ".zst", ".tzst", ".lz", ".lz4", ".lzma", ".cab", ".arj", ".br", ".zpaq",
            ".jar", ".war", ".apk", ".ipa", ".xpi", ".crx", ".epub",
            ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".vsdx",
            // Fonts (compressed)
            ".woff", ".woff2",
            // BoltZip's own format is already a compressed, authenticated container
            ".bz",
        };

    /// <summary>The share of already-compressed bytes at which the planner switches to a fast store path.</summary>
    public const double FastPathThreshold = 0.75;

    /// <summary>True when the file's extension marks it as already compressed.</summary>
    public static bool IsAlreadyCompressed(string path) =>
        AlreadyCompressedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Walks the given files and directories once, returning the total byte count and how
    /// many of those bytes belong to already-compressed files.
    /// </summary>
    public static InputContent Analyze(IReadOnlyList<string> inputs)
    {
        long total = 0;
        long incompressible = 0;
        var files = 0;
        var incompressibleFiles = 0;

        void Account(string file)
        {
            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch
            {
                return; // unreadable input; ignore during estimation
            }

            files++;
            total += length;
            if (IsAlreadyCompressed(file))
            {
                incompressibleFiles++;
                incompressible += length;
            }
        }

        foreach (var input in inputs)
        {
            try
            {
                var full = Path.GetFullPath(input);
                if (Directory.Exists(full))
                {
                    foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    {
                        Account(file);
                    }
                }
                else if (File.Exists(full))
                {
                    Account(full);
                }
            }
            catch
            {
                // ignore inputs that cannot be enumerated
            }
        }

        return new InputContent(total, incompressible, files, incompressibleFiles);
    }
}

/// <summary>The result of scanning inputs for size and compressibility.</summary>
public readonly record struct InputContent(
    long TotalBytes,
    long IncompressibleBytes,
    int FileCount,
    int IncompressibleFileCount)
{
    /// <summary>Fraction (0..1) of total bytes that belong to already-compressed files.</summary>
    public double IncompressibleRatio => TotalBytes > 0 ? (double)IncompressibleBytes / TotalBytes : 0;

    /// <summary>True when already-compressed media dominates the input and a fast store path is worthwhile.</summary>
    public bool IsIncompressibleDominated =>
        TotalBytes > 0 && IncompressibleRatio >= ContentAnalysis.FastPathThreshold;
}
