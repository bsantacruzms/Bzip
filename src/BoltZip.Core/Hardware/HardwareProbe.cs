using System.Runtime.InteropServices;
using System.Text.Json;
using BoltZip.Core.Infrastructure;
using ArmAes = System.Runtime.Intrinsics.Arm.Aes;
using X86Aes = System.Runtime.Intrinsics.X86.Aes;
using Avx2 = System.Runtime.Intrinsics.X86.Avx2;
using Avx512F = System.Runtime.Intrinsics.X86.Avx512F;

namespace BoltZip.Core.Hardware;

/// <summary>
/// Discovers the capabilities of the current machine. All discovery is best-effort and
/// never throws; missing information degrades gracefully to conservative defaults.
/// </summary>
public static class HardwareProbe
{
    /// <summary>
    /// Full detection including a discrete-GPU scan and system-drive classification.
    /// Involves one short-lived PowerShell query on Windows.
    /// </summary>
    public static async Task<HardwareProfile> DetectAsync(CancellationToken cancellationToken = default)
    {
        var (total, available) = GetMemory();
        var gpus = await DetectGpusAsync(cancellationToken).ConfigureAwait(false);
        var systemDrive = await StorageProbe
            .GetKindAsync(AppContext.BaseDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new HardwareProfile
        {
            Architecture = RuntimeInformation.ProcessArchitecture,
            LogicalCores = Environment.ProcessorCount,
            TotalMemoryBytes = total,
            AvailableMemoryBytes = available,
            SupportsHardwareAes = X86Aes.IsSupported || ArmAes.IsSupported,
            SupportsAvx2 = Avx2.IsSupported,
            SupportsAvx512 = Avx512F.IsSupported,
            SystemStorage = systemDrive,
            Gpus = gpus,
        };
    }

    /// <summary>
    /// Fast, fully in-process detection. Skips the GPU scan and returns an empty GPU list;
    /// storage is left as <see cref="StorageKind.Unknown"/>. Suitable for hot paths.
    /// </summary>
    public static HardwareProfile DetectFast()
    {
        var (total, available) = GetMemory();

        return new HardwareProfile
        {
            Architecture = RuntimeInformation.ProcessArchitecture,
            LogicalCores = Environment.ProcessorCount,
            TotalMemoryBytes = total,
            AvailableMemoryBytes = available,
            SupportsHardwareAes = X86Aes.IsSupported || ArmAes.IsSupported,
            SupportsAvx2 = Avx2.IsSupported,
            SupportsAvx512 = Avx512F.IsSupported,
            SystemStorage = StorageKind.Unknown,
            Gpus = Array.Empty<GpuInfo>(),
        };
    }

    private static (long Total, long Available) GetMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var status = new NativeMethods.MemoryStatusEx();
                if (NativeMethods.GlobalMemoryStatusEx(ref status))
                {
                    return ((long)status.TotalPhys, (long)status.AvailPhys);
                }
            }
            catch
            {
                // fall through to the managed estimate
            }
        }

        var info = GC.GetGCMemoryInfo();
        var total = info.TotalAvailableMemoryBytes;
        return (total, total);
    }

    private static async Task<IReadOnlyList<GpuInfo>> DetectGpusAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            return DetectLinuxGpus();
        }

        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<GpuInfo>();
        }

        const string script =
            "Get-CimInstance Win32_VideoController | " +
            "Select-Object Name, AdapterRAM, DriverVersion | ConvertTo-Json -Compress";

        var json = await ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -Command \"{script}\"",
            cancellationToken,
            timeoutMs: 7000).ConfigureAwait(false);

        return ParseGpuJson(json);
    }

    /// <summary>
    /// Reads graphics adapters from sysfs on Linux. Each DRM card exposes its PCI vendor id,
    /// which is enough to know whether a hardware video encoder (NVENC, Quick Sync) is present.
    /// </summary>
    private static IReadOnlyList<GpuInfo> DetectLinuxGpus()
    {
        var gpus = new List<GpuInfo>();
        try
        {
            const string drm = "/sys/class/drm";
            if (!Directory.Exists(drm))
            {
                return Array.Empty<GpuInfo>();
            }

            foreach (var card in Directory.EnumerateDirectories(drm, "card*"))
            {
                // Skip connector entries such as card0-HDMI-A-1.
                if (Path.GetFileName(card).Contains('-'))
                {
                    continue;
                }

                var vendorFile = Path.Combine(card, "device", "vendor");
                if (!File.Exists(vendorFile))
                {
                    continue;
                }

                var id = File.ReadAllText(vendorFile).Trim();
                var (vendor, name) = id.ToLowerInvariant() switch
                {
                    "0x10de" => (GpuVendor.Nvidia, "NVIDIA GPU"),
                    "0x1002" or "0x1022" => (GpuVendor.Amd, "AMD GPU"),
                    "0x8086" => (GpuVendor.Intel, "Intel GPU"),
                    _ => (GpuVendor.Unknown, "GPU"),
                };

                if (vendor != GpuVendor.Unknown && !gpus.Any(g => g.Vendor == vendor))
                {
                    gpus.Add(new GpuInfo(name, vendor, 0, null));
                }
            }
        }
        catch
        {
            // sysfs unreadable (container, permissions); fall back to no GPU.
            return Array.Empty<GpuInfo>();
        }

        return gpus;
    }

    internal static IReadOnlyList<GpuInfo> ParseGpuJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<GpuInfo>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var results = new List<GpuInfo>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    var gpu = ReadGpu(element);
                    if (gpu is not null)
                    {
                        results.Add(gpu);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var gpu = ReadGpu(root);
                if (gpu is not null)
                {
                    results.Add(gpu);
                }
            }

            return results;
        }
        catch
        {
            return Array.Empty<GpuInfo>();
        }
    }

    private static GpuInfo? ReadGpu(JsonElement element)
    {
        if (!element.TryGetProperty("Name", out var nameProp))
        {
            return null;
        }

        var name = nameProp.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        long memory = 0;
        if (element.TryGetProperty("AdapterRAM", out var ramProp) &&
            ramProp.ValueKind == JsonValueKind.Number &&
            ramProp.TryGetInt64(out var ram) && ram > 0)
        {
            memory = ram;
        }

        string? driver = null;
        if (element.TryGetProperty("DriverVersion", out var driverProp))
        {
            driver = driverProp.GetString();
        }

        return new GpuInfo(name.Trim(), GpuInfo.ParseVendor(name), memory, driver);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;

            public MemoryStatusEx()
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
                MemoryLoad = 0;
                TotalPhys = 0;
                AvailPhys = 0;
                TotalPageFile = 0;
                AvailPageFile = 0;
                TotalVirtual = 0;
                AvailVirtual = 0;
                AvailExtendedVirtual = 0;
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
