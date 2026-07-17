namespace BoltZip.Core.Compression;

/// <summary>The current phase of an archive operation.</summary>
public enum ArchivePhase
{
    Scanning,
    Compressing,
    Extracting,
    Finalizing,
    Done,
}

/// <summary>
/// Progress snapshot reported during create/extract. Byte totals may be estimates when
/// the final size is not yet known (e.g. streaming compression).
/// </summary>
public sealed record ArchiveProgress(
    ArchivePhase Phase,
    string? CurrentEntry,
    long ProcessedBytes,
    long TotalBytes,
    int EntriesDone,
    int EntriesTotal)
{
    public double Percent =>
        TotalBytes > 0
            ? Math.Clamp(ProcessedBytes * 100d / TotalBytes, 0d, 100d)
            : (EntriesTotal > 0 ? Math.Clamp(EntriesDone * 100d / EntriesTotal, 0d, 100d) : 0d);
}
