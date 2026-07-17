namespace BoltZip.Core.Compression;

/// <summary>Metadata about a single entry inside an archive, used for listing.</summary>
public sealed record ArchiveEntryInfo(
    string Path,
    long Size,
    long CompressedSize,
    bool IsDirectory,
    bool IsEncrypted,
    DateTime? Modified)
{
    public double Ratio => Size > 0 ? (double)CompressedSize / Size : 0d;
}
