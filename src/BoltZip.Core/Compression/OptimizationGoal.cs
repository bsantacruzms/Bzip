namespace BoltZip.Core.Compression;

/// <summary>
/// The user's intent for a compression job. The optimization planner turns this,
/// combined with the detected hardware, into concrete codec settings.
/// </summary>
public enum OptimizationGoal
{
    /// <summary>Fastest reasonable compression. Prioritizes throughput over ratio.</summary>
    MaxSpeed,

    /// <summary>Sensible default: strong ratio while staying fast on modern hardware.</summary>
    Balanced,

    /// <summary>Smallest possible output. Prioritizes ratio over time.</summary>
    MaxRatio,
}
