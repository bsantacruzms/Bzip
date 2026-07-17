namespace BoltZip.Core.Hardware;

/// <summary>
/// The physical/logical nature of a storage location. Drives I/O buffer sizing and
/// how aggressively the engine parallelizes reads and writes.
/// </summary>
public enum StorageKind
{
    Unknown = 0,

    /// <summary>Spinning magnetic disk. Seeks are expensive; prefer sequential, low parallelism.</summary>
    Hdd,

    /// <summary>SATA/other solid-state disk. Handles moderate parallel I/O well.</summary>
    Ssd,

    /// <summary>NVMe solid-state disk. Very high queue depth and throughput.</summary>
    Nvme,

    /// <summary>Network share / UNC path. Favor large buffers and low parallelism.</summary>
    Network,

    /// <summary>Removable media (USB flash, external drive).</summary>
    Removable,

    /// <summary>Optical media (CD/DVD/Blu-ray).</summary>
    Optical,

    /// <summary>RAM disk or memory-backed location.</summary>
    Ram,
}
