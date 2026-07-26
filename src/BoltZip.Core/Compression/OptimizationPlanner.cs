using BoltZip.Core.Hardware;

namespace BoltZip.Core.Compression;

/// <summary>
/// Turns a hardware profile plus an <see cref="OptimizationGoal"/> into concrete codec
/// settings. This is the "auto-optimization" brain: it picks thread counts, match window
/// sizes, I/O buffers and encryption acceleration based on the machine and the workload.
/// </summary>
public static class OptimizationPlanner
{
    private const long KiB = 1024;
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024 * 1024 * 1024;

    public static CompressionPlan Plan(
        HardwareProfile hardware,
        OptimizationGoal goal,
        ArchiveFormat format,
        long? inputSizeBytes = null,
        StorageKind targetStorage = StorageKind.Unknown,
        double incompressibleRatio = 0)
    {
        var storage = targetStorage == StorageKind.Unknown ? hardware.SystemStorage : targetStorage;
        var rationale = new List<string>();

        var level = BaseLevel(goal, format);
        rationale.Add($"Goal '{goal}' → codec level {level} for {format}.");

        // When the input is dominated by already-compressed media (video, photos, music,
        // existing archives), a general-purpose codec can only shave a fraction of a percent
        // while spending most of its time searching for matches that do not exist. Drop to a
        // fast store-level so the data is packed at maximum speed with no change in quality.
        var mediaFastPath = incompressibleRatio >= ContentAnalysis.FastPathThreshold;
        if (mediaFastPath)
        {
            level = FastestLevel(format);
            rationale.Add(
                $"Input is {incompressibleRatio * 100:0.#}% already-compressed media → " +
                $"fast store-level {level} (media does not shrink; this maximizes speed with no quality loss).");
        }

        var threads = PlanThreads(hardware, goal, storage, inputSizeBytes, rationale);
        var (windowBytes, ldm) = PlanWindow(hardware, goal, format, level, threads, inputSizeBytes, rationale);
        var buffer = PlanBuffer(storage, inputSizeBytes, rationale);

        var hwAes = hardware.SupportsHardwareAes;
        rationale.Add(hwAes
            ? "Hardware AES available → accelerated encryption path enabled."
            : "No hardware AES → using software XChaCha20 (fast without AES-NI).");

        if (hardware.HasDiscreteGpu)
        {
            rationale.Add($"Discrete GPU detected ({hardware.PrimaryDiscreteGpu!.Name}) → eligible for experimental GPU offload.");
        }

        return new CompressionPlan
        {
            Goal = goal,
            Level = level,
            WorkerThreads = threads,
            WindowBytes = windowBytes,
            BufferBytes = buffer,
            LongDistanceMatching = ldm,
            HardwareAes = hwAes,
            MediaFastPath = mediaFastPath,
            Rationale = rationale,
        };
    }

    /// <summary>The fastest useful level for a codec, used when the input barely compresses.</summary>
    private static int FastestLevel(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Brotli => 1,
        _ => 1, // zstd (.bz/.zst), deflate (zip/gzip) and bzip2 are all fastest at level 1
    };


    private static int BaseLevel(OptimizationGoal goal, ArchiveFormat format)
    {
        // Zstandard family (BoltZip native + .zst): scale 1..22.
        if (format is ArchiveFormat.Bz or ArchiveFormat.Zstd)
        {
            return goal switch
            {
                OptimizationGoal.MaxSpeed => 3,
                OptimizationGoal.Balanced => 12,
                _ => 19,
            };
        }

        // Brotli: scale 0..11.
        if (format is ArchiveFormat.Brotli)
        {
            return goal switch
            {
                OptimizationGoal.MaxSpeed => 4,
                OptimizationGoal.Balanced => 9,
                _ => 11,
            };
        }

        // Bzip2: block-size factor 1..9.
        if (format is ArchiveFormat.Bzip2)
        {
            return goal switch
            {
                OptimizationGoal.MaxSpeed => 5,
                _ => 9,
            };
        }

        // Deflate family (zip/gzip): scale 1..9.
        return goal switch
        {
            OptimizationGoal.MaxSpeed => 1,
            OptimizationGoal.Balanced => 6,
            _ => 9,
        };
    }

    private static int PlanThreads(
        HardwareProfile hardware,
        OptimizationGoal goal,
        StorageKind storage,
        long? inputSizeBytes,
        List<string> rationale)
    {
        var cores = Math.Max(1, hardware.LogicalCores);
        var threads = cores;

        var storageCap = storage switch
        {
            StorageKind.Hdd => 4,
            StorageKind.Network => 2,
            StorageKind.Removable => 4,
            StorageKind.Optical => 1,
            _ => cores,
        };

        if (storageCap < threads)
        {
            threads = storageCap;
            rationale.Add($"{storage} target → capping workers to {threads} to avoid I/O contention.");
        }
        else
        {
            rationale.Add($"{cores} logical cores → up to {threads} compression workers.");
        }

        if (inputSizeBytes is > 0)
        {
            // Give each worker at least ~2 MiB of useful work.
            var byWork = (int)Math.Max(1, inputSizeBytes.Value / (2 * MiB));
            if (byWork < threads)
            {
                threads = byWork;
                rationale.Add($"Small input ({FormatBytes(inputSizeBytes.Value)}) → reducing workers to {threads}.");
            }
        }

        return Math.Max(1, threads);
    }

    private static (long WindowBytes, bool Ldm) PlanWindow(
        HardwareProfile hardware,
        OptimizationGoal goal,
        ArchiveFormat format,
        int level,
        int threads,
        long? inputSizeBytes,
        List<string> rationale)
    {
        // Non-zstd codecs have fixed or level-derived windows; report them for display only.
        switch (format)
        {
            case ArchiveFormat.Zip:
            case ArchiveFormat.Gzip:
                return (32 * KiB, false);
            case ArchiveFormat.Bzip2:
                return (100_000L * level, false);
            case ArchiveFormat.Brotli:
                return (1L << 22, false); // default lgwin 22 (4 MiB)
        }

        // Zstandard family: choose a window log by level, then clamp to input and RAM.
        var windowLog = level switch
        {
            <= 5 => 21,
            <= 9 => 23,
            <= 12 => 24,
            <= 16 => 25,
            <= 19 => 26,
            _ => 27,
        };

        if (inputSizeBytes is > 0)
        {
            var inputLog = CeilLog2(inputSizeBytes.Value);
            if (inputLog < windowLog)
            {
                windowLog = Math.Max(18, inputLog);
                rationale.Add($"Window capped to input size (~{FormatBytes(1L << windowLog)}).");
            }
        }

        // Keep compression memory within a fraction of available RAM.
        var budget = hardware.AvailableMemoryBytes > 0
            ? hardware.AvailableMemoryBytes / 4
            : 512 * MiB;

        // Rough zstd working-set estimate: ~10x window per worker at high levels.
        const long perWindowFactor = 10;
        while (windowLog > 18)
        {
            var estimate = (1L << windowLog) * perWindowFactor * threads;
            if (estimate <= budget)
            {
                break;
            }

            windowLog--;
        }

        var windowBytes = 1L << windowLog;
        rationale.Add($"Match window {FormatBytes(windowBytes)} (fits within {FormatBytes(budget)} RAM budget for {threads} workers).");

        var largeInput = inputSizeBytes is null || inputSizeBytes.Value >= 256 * MiB;
        var ldm = goal != OptimizationGoal.MaxSpeed && windowBytes >= 8 * MiB && largeInput;
        if (ldm)
        {
            rationale.Add("Long-distance matching enabled for large/redundant input.");
        }

        return (windowBytes, ldm);
    }

    private static int PlanBuffer(StorageKind storage, long? inputSizeBytes, List<string> rationale)
    {
        var buffer = storage switch
        {
            StorageKind.Nvme => (int)(4 * MiB),
            StorageKind.Ssd => (int)(1 * MiB),
            StorageKind.Hdd => (int)(512 * KiB),
            StorageKind.Network => (int)(8 * MiB),
            StorageKind.Removable => (int)(1 * MiB),
            _ => (int)(256 * KiB),
        };

        if (inputSizeBytes is > 0 && inputSizeBytes.Value < buffer)
        {
            buffer = (int)Math.Max(64 * KiB, inputSizeBytes.Value);
        }

        rationale.Add($"{storage} target → {FormatBytes(buffer)} I/O buffer.");
        return buffer;
    }

    private static int CeilLog2(long value)
    {
        if (value <= 1)
        {
            return 0;
        }

        var log = 0;
        var v = value - 1;
        while (v > 0)
        {
            v >>= 1;
            log++;
        }

        return log;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= GiB)
        {
            return $"{bytes / (double)GiB:0.#} GiB";
        }

        if (bytes >= MiB)
        {
            return $"{bytes / (double)MiB:0.#} MiB";
        }

        if (bytes >= KiB)
        {
            return $"{bytes / (double)KiB:0.#} KiB";
        }

        return $"{bytes} B";
    }
}
