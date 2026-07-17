namespace BoltZip.Core.Hardware;

/// <summary>Best-effort classification of a graphics adapter vendor.</summary>
public enum GpuVendor
{
    Unknown = 0,
    Nvidia,
    Amd,
    Intel,
    Apple,
    Microsoft,
}

/// <summary>
/// A discovered graphics adapter. GPU memory reported by the OS can be inaccurate for
/// adapters larger than 4 GB, so <see cref="MemoryBytes"/> is advisory only.
/// </summary>
public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor,
    long MemoryBytes,
    string? DriverVersion)
{
    /// <summary>True for a dedicated adapter capable of hosting compute (non-Intel-iGPU, non-basic).</summary>
    public bool IsDiscrete =>
        Vendor is GpuVendor.Nvidia or GpuVendor.Amd &&
        !Name.Contains("Basic", StringComparison.OrdinalIgnoreCase);

    public static GpuVendor ParseVendor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return GpuVendor.Unknown;
        }

        if (name.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("quadro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rtx", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tesla", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Nvidia;
        }

        if (name.Contains("amd", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ryzen", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Amd;
        }

        if (name.Contains("intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("arc", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Intel;
        }

        if (name.Contains("apple", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Apple;
        }

        if (name.Contains("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Microsoft;
        }

        return GpuVendor.Unknown;
    }
}
