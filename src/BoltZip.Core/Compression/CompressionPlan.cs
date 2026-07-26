namespace BoltZip.Core.Compression;

/// <summary>
/// The concrete, hardware-tuned settings the engine will use for a compression job.
/// Produced by <see cref="OptimizationPlanner"/>. <see cref="Rationale"/> explains the
/// choices so the UI/CLI can show <i>why</i> a configuration was selected.
/// </summary>
public sealed record CompressionPlan
{
    public required OptimizationGoal Goal { get; init; }

    /// <summary>Codec-normalized effort level (higher = smaller/slower).</summary>
    public required int Level { get; init; }

    /// <summary>Number of worker threads to use for compression.</summary>
    public required int WorkerThreads { get; init; }

    /// <summary>Match window / dictionary size in bytes (codec dependent).</summary>
    public required long WindowBytes { get; init; }

    /// <summary>I/O buffer size in bytes, tuned to the target storage.</summary>
    public required int BufferBytes { get; init; }

    /// <summary>Enable long-distance matching (helps large, redundant inputs).</summary>
    public required bool LongDistanceMatching { get; init; }

    /// <summary>True when hardware-accelerated AES is available for encrypted formats.</summary>
    public required bool HardwareAes { get; init; }

    /// <summary>
    /// True when the input is dominated by already-compressed media (video, photos, music,
    /// existing archives). The planner then favors a fast store-level pass: compressing such
    /// data is wasted effort, so BoltZip packs it at maximum speed with no quality loss.
    /// </summary>
    public bool MediaFastPath { get; init; }

    /// <summary>Human-readable explanation of the tuning decisions.</summary>
    public required IReadOnlyList<string> Rationale { get; init; }

    public double WindowMiB => WindowBytes / (1024d * 1024d);
}
