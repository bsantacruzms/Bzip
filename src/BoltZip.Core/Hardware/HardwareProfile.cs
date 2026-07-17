using System.Runtime.InteropServices;

namespace BoltZip.Core.Hardware;

/// <summary>
/// A snapshot of the machine BoltZip is running on. Produced by <see cref="HardwareProbe"/>
/// and consumed by the optimization planner to auto-tune compression settings.
/// </summary>
public sealed record HardwareProfile
{
    /// <summary>Process architecture (X64, Arm64, ...).</summary>
    public required Architecture Architecture { get; init; }

    /// <summary>Number of logical processors available to the process.</summary>
    public required int LogicalCores { get; init; }

    /// <summary>Total physical RAM in bytes (best-effort; 0 when unknown).</summary>
    public required long TotalMemoryBytes { get; init; }

    /// <summary>Currently available physical RAM in bytes (best-effort; 0 when unknown).</summary>
    public required long AvailableMemoryBytes { get; init; }

    /// <summary>True when the CPU exposes hardware AES (AES-NI on x86, crypto extensions on ARM).</summary>
    public required bool SupportsHardwareAes { get; init; }

    /// <summary>True when AVX2 is available (used as a proxy for wide-SIMD codec paths).</summary>
    public required bool SupportsAvx2 { get; init; }

    /// <summary>True when AVX-512 foundation is available.</summary>
    public required bool SupportsAvx512 { get; init; }

    /// <summary>Kind of storage backing the system/OS drive.</summary>
    public required StorageKind SystemStorage { get; init; }

    /// <summary>Discovered graphics adapters (may be empty).</summary>
    public required IReadOnlyList<GpuInfo> Gpus { get; init; }

    public bool HasDiscreteGpu => Gpus.Any(g => g.IsDiscrete);

    public GpuInfo? PrimaryDiscreteGpu => Gpus.FirstOrDefault(g => g.IsDiscrete);

    public double TotalMemoryGiB => TotalMemoryBytes / (1024d * 1024d * 1024d);

    public double AvailableMemoryGiB => AvailableMemoryBytes / (1024d * 1024d * 1024d);

    /// <summary>A short, human-readable one-liner for logs and the UI footer.</summary>
    public string Summary()
    {
        var gpu = PrimaryDiscreteGpu?.Name ?? (Gpus.Count > 0 ? Gpus[0].Name : "no GPU");
        var aes = SupportsHardwareAes ? "AES-NI" : "no AES-NI";
        var simd = SupportsAvx512 ? "AVX-512" : SupportsAvx2 ? "AVX2" : "SSE";
        return $"{Architecture}, {LogicalCores} cores, {TotalMemoryGiB:0.#} GiB RAM, " +
               $"{SystemStorage}, {simd}, {aes}, {gpu}";
    }
}
