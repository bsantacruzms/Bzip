using BoltZip.Core.Hardware;

namespace BoltZip.Core.Media;

/// <summary>How aggressively to shrink a video. Higher quality keeps more detail and a larger file.</summary>
public enum VideoQuality
{
    /// <summary>Re-encode at a quality that is indistinguishable from the source to the eye.</summary>
    VisuallyLossless,

    /// <summary>A strong size reduction with quality that is still excellent for everyday viewing.</summary>
    Balanced,

    /// <summary>Smallest files for quick sharing; some fine detail is traded away.</summary>
    Smaller,
}

/// <summary>The video codec to encode into. <see cref="Auto"/> picks the best broadly-compatible option.</summary>
public enum VideoCodec
{
    /// <summary>Choose automatically: HEVC (H.265), a great balance of size, quality and device support.</summary>
    Auto,

    /// <summary>H.265 / HEVC. ~40-50% smaller than H.264 at the same quality; plays on most modern devices.</summary>
    Hevc,

    /// <summary>AV1. Smallest of the three, ideal on newer hardware/players; encodes fast on RTX 40/50 GPUs.</summary>
    Av1,

    /// <summary>H.264 / AVC. Largest, but plays literally everywhere.</summary>
    H264,
}

/// <summary>A concrete encoder choice (an FFmpeg encoder name plus how to describe it).</summary>
public sealed record VideoEncoder(
    string FfmpegName,
    string DisplayName,
    VideoCodec Codec,
    bool IsHardware,
    GpuVendor Vendor);

/// <summary>
/// The resolved encoding strategy: a primary encoder (hardware-accelerated when possible) and a
/// software fallback used if the hardware encoder fails to initialize at runtime.
/// </summary>
public sealed record VideoEncodePlan(
    VideoEncoder Primary,
    VideoEncoder CpuFallback,
    VideoQuality Quality,
    IReadOnlyList<string> Rationale);

/// <summary>The outcome of shrinking one video.</summary>
public sealed record VideoEncodeResult(
    string InputPath,
    string OutputPath,
    long InputBytes,
    long OutputBytes,
    TimeSpan Elapsed,
    VideoEncoder EncoderUsed,
    bool UsedFallback)
{
    /// <summary>Fraction (0..1) of size removed. 0.72 means the output is 72% smaller.</summary>
    public double Reduction => InputBytes > 0 ? 1d - (double)OutputBytes / InputBytes : 0;
}

/// <summary>Progress for a running encode.</summary>
public readonly record struct VideoProgress(string File, double Percent, double Speed);

/// <summary>Video container extensions BoltZip will re-encode.</summary>
public static class VideoFormats
{
    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm",
            ".mpg", ".mpeg", ".m2v", ".mts", ".m2ts", ".ts", ".3gp", ".3g2",
            ".ogv", ".vob", ".asf", ".divx", ".f4v",
        };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));
}
